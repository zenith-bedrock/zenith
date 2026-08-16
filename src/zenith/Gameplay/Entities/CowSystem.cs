using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXII — fourth ECS-authoritative actor, and the first passive one. Position/Health/
/// ActorIdentity/DespawnTracking live in <see cref="EntityRuntime"/>'s shared component stores;
/// only <see cref="CowState"/> (wander heading/timer, breed cooldown) is feature-specific and owned
/// here. Combat/loot/XP bookkeeping goes through <see cref="DamageableActorCombat"/>, same as
/// Zombie/Minecart. Cow's AI wanders and never targets or attacks, so nothing here ever reads or
/// writes <c>Velocity.X</c>/<c>Velocity.Z</c> (unlike Zombie's knockback or Minecart's rolling
/// motion) — but Phase XXIX attaches the component anyway and reuses <c>Velocity.Y</c> as every
/// ground mob's gravity fall-speed, the same shared-data reuse <see cref="Velocity"/>'s own doc
/// comment describes.
/// </summary>
sealed class CowSystem : IGameSystem
{
    private const float SpawnDistance = 4f;
    private const float MovePerTick = 0.05f;
    private const int MinWanderTicks = 40;
    private const int MaxWanderTicks = 100;
    private const string LootItemName = "minecraft:beef";
    private const float AttackDamage = 4f;
    /// <summary>Vanilla-parity passive-mob kill reward — lower than a hostile mob's (Phase XI.4/XIV).</summary>
    private const int KillExperience = 1;

    // Phase XVI — proximity auto-breeding. Phase XVIII added a real player-feed trigger on top
    // (see TryHandleFeed) — the gap both this and Villager's trade were deferred for since Phase XVI.
    private const float BreedRadius = 2f;
    private const int BreedCooldownTicks = 1200; // 60s @ 20 TPS before either parent (or the calf) can breed again.
    /// <summary>A concrete, local population cap — not a general "mob limit" system (Phase XVI world-lifecycle probe).</summary>
    private const int MaxPopulation = 32;
    private const float DespawnRadius = 64f;
    private const float InteractDistance = 2.25f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<CowState> _cows;
    private readonly StackId _lootItem;
    private readonly StackId _feedItem;
    private readonly Random _random;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.
    private bool _bootstrapSpawned;

    internal long FeedCount { get; private set; }

    public CowSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _cows = new ComponentStore<CowState>(stores.Entities);
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _feedItem = StackId.FromItem(itemPalette.Require("minecraft:wheat"));
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Cows => _cows.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<CowState> CowStates => _cows;

    /// <summary>Cross-species dispatch seam (Phase XXI/XXII) — "is this ECS entity a cow," nothing more.</summary>
    internal bool Owns(EntityId id) => _cows.Has(id);
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long BirthCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_cows.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapCow(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_cows.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;
            TryHandleFeed(id, online, clock);
            Wander(id, clock);
            ApplyGravity(id);
            ReconcileViewers(id, online);
        }

        TryBreed(clock);

        _replicated.RemoveWhere(pair => !IsKnownAliveCowId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private bool IsKnownAliveCowId(long actorUniqueId)
    {
        foreach (var id in _cows.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    /// <summary>
    /// The feature-specific composition step every migrated actor needs on top of
    /// <see cref="EntityRuntime.CreateActor"/> — used by both bootstrap spawn and <see cref="SpawnCalf"/>.
    /// </summary>
    internal EntityId SpawnCow(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Cow.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(10f) }); // Vanilla-parity cow health.
        _stores.Velocities.Set(id, new Velocity()); // Phase XXIX: Y reused as gravity fall-speed.
        _stores.Despawn.Set(id, new DespawnTracking());
        _cows.Set(id, new CowState());
        return id;
    }

    /// <summary>
    /// Proximity-only auto-breeding (Phase XVI): no growth-to-adult stage. At most one birth per
    /// tick keeps this bounded and simple to reason about.
    /// </summary>
    private void TryBreed(GameClock clock)
    {
        if (_cows.Count >= MaxPopulation) return;
        var candidates = _cows.Entities;
        for (var i = 0; i < candidates.Count; i++)
        {
            var a = candidates[i];
            if (!_cows.TryGet(a, out var stateA) || clock.CurrentTick < stateA.BreedCooldownUntilTick) continue;
            if (!_stores.Positions.TryGet(a, out var posA)) continue;
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var b = candidates[j];
                if (!_cows.TryGet(b, out var stateB) || clock.CurrentTick < stateB.BreedCooldownUntilTick) continue;
                if (!_stores.Positions.TryGet(b, out var posB)) continue;
                var dx = posA.X - posB.X;
                var dz = posA.Z - posB.Z;
                if (dx * dx + dz * dz > BreedRadius * BreedRadius) continue;

                stateA.BreedCooldownUntilTick = clock.CurrentTick + BreedCooldownTicks;
                stateB.BreedCooldownUntilTick = clock.CurrentTick + BreedCooldownTicks;
                _cows.Set(a, stateA);
                _cows.Set(b, stateB);
                SpawnCalf(posA, posB, clock);
                return;
            }
        }
    }

    private void SpawnCalf(Position parentA, Position parentB, GameClock clock)
    {
        var x = (parentA.X + parentB.X) / 2f;
        var z = (parentA.Z + parentB.Z) / 2f;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        var id = SpawnCow(x, y, z);
        ref var state = ref _cows.GetRef(id);
        state.BreedCooldownUntilTick = clock.CurrentTick + BreedCooldownTicks;
        BirthCount++;
    }

    /// <summary>
    /// Phase XVIII, Priority 1 — the second real consumer of
    /// <c>InventoryTransactionPacket.ActorInteract</c> (see VillagerSystem's trade for the first).
    /// Feeding wheat clears the cow's breed cooldown immediately rather than waiting out
    /// <see cref="BreedCooldownTicks"/>; <see cref="TryBreed"/> still owns whether/when a birth
    /// actually happens.
    /// </summary>
    private void TryHandleFeed(EntityId id, IReadOnlyList<Player.Player> online, GameClock clock)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var reachSquared = InteractDistance * InteractDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = pos.X - player.PositionX;
            var dz = pos.Z - player.PositionZ;
            if (dx * dx + dz * dz > reachSquared) continue;
            if (!_stores.Identities.TryGet(id, out var identity)) continue;
            if (!player.TryConsumeInteractIntent(checked((long)identity.ActorRuntimeId))) continue;

            if (player.Inventory.TryConsume(_feedItem, 1))
            {
                player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
                player.Session.Context.World.PersistInventory(player);
                ref var state = ref _cows.GetRef(id);
                state.BreedCooldownUntilTick = clock.CurrentTick;
                FeedCount++;
            }
            return; // At most one interact resolved per tick — the intent itself is already one-shot.
        }
    }

    private void EnsureBootstrapCow(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _cows.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX - SpawnDistance;
        var z = player.PositionZ;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        SpawnCow(x, y, z);
        _bootstrapSpawned = true;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return false;
        ref var tracking = ref _stores.Despawn.GetRef(id);
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            pos.X, pos.Z, online, DespawnRadius, clock.CurrentTick, tracking.LastSeenNearPlayerTick);
        tracking.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        if (!_stores.Identities.TryGet(id, out var identity)) return false;
        foreach (var peer in online)
            if (_replicated.Remove((identity.ActorUniqueId, peer.RuntimeId)))
            {
                _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
                ReplicatedRemovalCount++;
            }
        _stores.DestroyActor(identity.ActorRuntimeId, id);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        if (!_stores.Health.TryGet(id, out var healthComponent)) return;
        var health = healthComponent.State;

        ViewerReconciliation.Sync(
            identity.ActorUniqueId, online, _replicated,
            peer => ActorInterest.Includes(peer, pos.X, pos.Z),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddCow(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
                peer.Session.Protocol.Entity.SendHealth(identity.ActorRuntimeId, health.Current, health.Maximum);
                _lastProjected[(identity.ActorUniqueId, peer.RuntimeId)] = new ProjectedPose(pos.X, pos.Y, pos.Z, pos.Yaw);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
                ReplicatedRemovalCount++;
            });
    }

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, 2.25f, AttackDamage, currentTick, TryApplyDamage);

    /// <summary>Concrete Cow health/removal operation — same shape as Zombie/Minecart's, one loot item, no retaliation.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Cow", currentTick,
            destroyActor: cid =>
            {
                if (_stores.Identities.TryGet(cid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, cid);
            },
            onDeathReplicatedToPeer: peer =>
            {
                if (_stores.Identities.TryGet(id, out var identity))
                    _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                ReplicatedRemovalCount++;
            });

    /// <summary>Peaceful AI: pick a random heading periodically, walk it, retry sooner if blocked.</summary>
    private void Wander(EntityId id, GameClock clock)
    {
        if (!_cows.TryGet(id, out var state)) return;
        if (clock.CurrentTick >= state.WanderChangeAtTick)
            PickNewHeading(id, clock);
        if (!_cows.TryGet(id, out state)) return;

        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var desiredX = pos.X + state.WanderDirectionX * MovePerTick;
        var desiredZ = pos.Z + state.WanderDirectionZ * MovePerTick;
        if (!TryMove(id, desiredX, desiredZ))
        {
            ref var retryState = ref _cows.GetRef(id);
            retryState.WanderChangeAtTick = clock.CurrentTick; // blocked — choose a fresh heading next tick
            return;
        }

        // Turned smoothly toward the wander heading every tick (Phase XXIII), not snapped once when
        // PickNewHeading picks it — this is what makes a passive mob's rotation read as natural.
        ref var p = ref _stores.Positions.GetRef(id);
        var desiredYaw = LookMath.YawTowards(state.WanderDirectionX, state.WanderDirectionZ);
        p.Yaw = LookMath.MoveYawTowards(p.Yaw, desiredYaw, LookMath.DefaultMaxTurnDegreesPerTick);
    }

    private void PickNewHeading(EntityId id, GameClock clock)
    {
        var angle = _random.NextSingle() * MathF.PI * 2f;
        ref var state = ref _cows.GetRef(id);
        state.WanderDirectionX = MathF.Cos(angle);
        state.WanderDirectionZ = MathF.Sin(angle);
        state.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);

    }

    /// <summary>Concrete Cow rule: where to step. Validity itself is shared (<see cref="GroundMobMovement"/>, Phase XV).</summary>
    private bool TryMove(EntityId id, float x, float z)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return false;
        if (!GroundMobMovement.TryMoveHorizontal(_world, pos.X, pos.Y, pos.Z, x, z, out var resolvedY)) return false;
        ref var p = ref _stores.Positions.GetRef(id);
        p.X = x;
        p.Y = resolvedY;
        p.Z = z;
        return true;
    }

    /// <summary>Phase XXIX: one tick of gravity/falling/landing, reusing <see cref="Velocity.Y"/> as a downward fall-speed magnitude.</summary>
    private void ApplyGravity(EntityId id)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        ref var vel = ref _stores.Velocities.GetRef(id);
        var y = pos.Y;
        if (!GroundMobMovement.ResolveVertical(_world, pos.X, pos.Z, ref y, ref vel.Y, out _)) return;
        ref var p = ref _stores.Positions.GetRef(id);
        p.Y = y;
    }

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var id in _cows.Entities)
            {
                if (!_stores.Identities.TryGet(id, out var identity)) continue;
                var key = (identity.ActorUniqueId, peer.RuntimeId);
                if (!_replicated.Contains(key)) continue;
                if (!_stores.Positions.TryGet(id, out var pos)) continue;
                var current = new ProjectedPose(pos.X, pos.Y, pos.Z, pos.Yaw);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                {
                    ReplicatedMoveSkippedCount++;
                    continue;
                }
                _moveBatch.Add(new RawActorPose
                {
                    ActorRuntimeId = identity.ActorRuntimeId,
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z,
                    Yaw = pos.Yaw,
                    HeadYaw = pos.Yaw
                });
                _lastProjected[key] = current;
                ReplicatedMoveCount++;
            }
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }
}
