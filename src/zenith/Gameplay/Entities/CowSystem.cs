using Zenith.Gameplay.Ports;
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
    private const float AttackDamage = 4f;

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
    private readonly MobCore _mob;
    private readonly GroundMobBody _body;
    private readonly StackId _feedItem;
    private readonly Random _random;
    private readonly ActorReplication _replication;
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.

    internal long FeedCount { get; private set; }

    public CowSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Cow, world, players, stores, itemPalette);
        _replication = _mob.Replication;
        _body = new GroundMobBody(world, stores, players);
        _cows = new ComponentStore<CowState>(stores.Entities);
        _feedItem = StackId.FromItem(itemPalette.Require("minecraft:wheat"));
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Cows => _cows.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<CowState> CowStates => _cows;

    /// <summary>Cross-species dispatch seam (Phase XXI/XXII) — "is this ECS entity a cow," nothing more.</summary>
    internal bool Owns(EntityId id) => _cows.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedMoveSkippedCount => _replication.MoveSkippedCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long BirthCount { get; private set; }
    internal long DespawnCount => _replication.DespawnCount;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;

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
            _body.ApplyGravity(id);
            ReconcileViewers(id, online);
        }

        TryBreed(clock);

        _replication.Prune(_cows.Entities, online);
        _replication.ReplicateMoves(_cows.Entities, online);
    }


    /// <summary>
    /// The feature-specific composition step every migrated actor needs on top of
    /// <see cref="EntityRuntime.CreateActor"/> — used by <see cref="SpawnCalf"/>.
    /// </summary>
    internal EntityId SpawnCow(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
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
                player.Client.Inventory.SendInventoryContent(player.Inventory);
                player.Level.PersistInventory(player);
                ref var state = ref _cows.GetRef(id);
                state.BreedCooldownUntilTick = clock.CurrentTick;
                FeedCount++;
            }
            return; // At most one interact resolved per tick — the intent itself is already one-shot.
        }
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online) =>
        _replication.TryDespawn(id, clock, online, DespawnRadius);

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online) =>
        _replication.Reconcile(id, online);

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, 2.25f, AttackDamage, currentTick, TryApplyDamage);

    /// <summary>Concrete Cow health/removal operation — same shape as Zombie/Minecart's, one loot item, no retaliation.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.TryApplyDamage(id, source, amount, online, currentTick);

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
        if (!_body.TryMove(id, desiredX, desiredZ))
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



}
