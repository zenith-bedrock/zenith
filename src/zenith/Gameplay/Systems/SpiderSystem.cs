using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Phase XXII — sixth ECS-authoritative actor. Position/Health/ActorIdentity/DespawnTracking live
/// in <see cref="EntityRuntime"/>'s shared component stores; only <see cref="SpiderState"/>
/// (retained target, attack cooldown) is feature-specific and owned here — same shape as
/// <see cref="ZombieState"/>, kept separate for the same reason described there. No
/// <see cref="Velocity"/> component: Spider never had knockback (that stayed Zombie-only since
/// Phase XVI) and nothing here reads or writes one.
///
/// <see cref="TryApplyPoison"/> is unchanged by this migration — it always operated on the target
/// <em>Player</em>'s <c>Effects</c> dictionary, never on Spider's own state, so ECS migration had
/// nothing to touch there. This is the concrete proof that a feature-specific post-hit consequence
/// (poison) stays feature-owned and does not need to become part of the ECS damage seam.
/// </summary>
sealed class SpiderSystem : IGameSystem
{
    private const float SpawnDistance = 6f;
    private const float DetectionDistance = 20f;
    private const float AttackDistance = 2.25f;
    private const float MovePerTick = 0.11f; // Faster than Zombie's 0.08 — a real behavioral difference, not just renaming.
    private const float AttackDamage = 2f; // Weaker per hit than Zombie, to keep overall threat comparable to a faster mob.
    private const int AttackCooldownTicks = 20;
    private const string LootItemName = "minecraft:string";
    private const int KillExperience = 5;
    private const float DespawnRadius = 64f;
    private const float PoisonChance = 0.3f;
    private const int PoisonDurationTicks = 100; // 5s @ 20 TPS — same cadence EffectSystem already ticks Poison at.

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<SpiderState> _spiders;
    private readonly StackId _lootItem;
    private readonly Random _random;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.
    private bool _bootstrapSpawned;

    public SpiderSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _spiders = new ComponentStore<SpiderState>(stores.Entities);
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Spiders => _spiders.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<SpiderState> SpiderStates => _spiders;

    /// <summary>Cross-species dispatch seam (Phase XXI/XXII) — "is this ECS entity a spider," nothing more.</summary>
    internal bool Owns(EntityId id) => _spiders.Has(id);
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }
    internal long PoisonInflictedCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_spiders.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapSpider(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_spiders.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;
            var target = FindOrAcquireTarget(id, online);
            if (target is not null)
                AdvanceTowardTarget(id, target);
            TryAttackPlayer(id, target, clock, online);
            ReconcileViewers(id, online);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveSpiderId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private bool IsKnownAliveSpiderId(long actorUniqueId)
    {
        foreach (var id in _spiders.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    private void EnsureBootstrapSpider(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _spiders.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX - SpawnDistance;
        var z = player.PositionZ - SpawnDistance;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        SpawnSpider(x, y, z);
        _bootstrapSpawned = true;
    }

    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnSpider(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Spider.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(16f) }); // Vanilla-parity: slightly less than Zombie's 20.
        _stores.Despawn.Set(id, new DespawnTracking());
        _spiders.Set(id, new SpiderState());
        return id;
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
                peer.Session.Protocol.Entity.SendAddSpider(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
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
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, currentTick, TryApplyDamage);

    private void TryAttackPlayer(EntityId id, Player.Player? target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _spiders.GetRef(id);
        if (state.NextAttackTick > clock.CurrentTick) return;
        if (target is null) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!IsTargetValid(pos, target, out var distanceSquared) || distanceSquared > AttackDistance * AttackDistance)
            return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        if (PlayerDamage.Apply(target, _players, online, DamageSource.MeleeFrom(identity.ActorUniqueId), AttackDamage,
                clock.CurrentTick, target.PositionX - pos.X, target.PositionZ - pos.Z))
        {
            state.NextAttackTick = clock.CurrentTick + AttackCooldownTicks;
            TryApplyPoison(target, clock);
            foreach (var peer in online)
                if (_replicated.Contains((identity.ActorUniqueId, peer.RuntimeId)))
                    peer.Session.Protocol.Entity.SendAttackSwing(identity.ActorRuntimeId);
        }
    }

    /// <summary>
    /// The mob side of a status effect: writes the same <c>Player.Effects</c> entry a potion or
    /// intent would (see <see cref="EffectSystem"/>'s <c>ApplyPendingIntent</c>), then replicates
    /// the same <see cref="MobEffectPacket"/>. Unchanged by ECS migration — see this class's doc
    /// comment.
    /// </summary>
    private void TryApplyPoison(Player.Player target, GameClock clock)
    {
        if (target.IsDead) return;
        if (_random.NextDouble() >= PoisonChance) return;

        var expiresAtTick = clock.CurrentTick + PoisonDurationTicks;
        if (!target.ApplyOrRefreshEffect(EffectType.Poison, 0, expiresAtTick, clock.CurrentTick)) return; // a longer poison is already active
        target.Session.Protocol.Entity.SendMobEffect(
            (ulong)target.RuntimeId, MobEffectPacket.EventAdd, (int)EffectType.Poison, 0, showParticles: true, PoisonDurationTicks);
        PoisonInflictedCount++;
    }

    /// <summary>Concrete Spider health/removal operation — same shape as every other migrated actor's.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Spider", currentTick,
            destroyActor: sid =>
            {
                if (_stores.Identities.TryGet(sid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, sid);
            },
            onDeathReplicatedToPeer: peer =>
            {
                if (_stores.Identities.TryGet(id, out var identity))
                    _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                ReplicatedRemovalCount++;
            });

    private Player.Player? FindOrAcquireTarget(EntityId id, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _spiders.GetRef(id);
        if (!_stores.Positions.TryGet(id, out var pos)) return null;

        if (state.TargetPlayerRuntimeId is { } retainedId)
        {
            var retained = online.FirstOrDefault(player => player.RuntimeId == retainedId);
            if (retained is not null && IsTargetValid(pos, retained, out _))
                return retained;
            state.TargetPlayerRuntimeId = null;
        }

        Player.Player? target = null;
        var best = DetectionDistance * DetectionDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = player.PositionX - pos.X;
            var dz = player.PositionZ - pos.Z;
            var distance = dx * dx + dz * dz;
            if (distance >= best) continue;
            best = distance;
            target = player;
        }

        state.TargetPlayerRuntimeId = target?.RuntimeId;
        return target;
    }

    private static bool IsTargetValid(Position pos, Player.Player target, out float distanceSquared)
    {
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        distanceSquared = dx * dx + dz * dz;
        return target.IsInGame && !target.IsDead && distanceSquared <= DetectionDistance * DetectionDistance;
    }

    private void AdvanceTowardTarget(EntityId id, Player.Player target)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!IsTargetValid(pos, target, out var best) ||
            best <= AttackDistance * AttackDistance || best <= 0.0001f)
            return;

        var length = MathF.Sqrt(best);
        var dxn = (target.PositionX - pos.X) / length;
        var dzn = (target.PositionZ - pos.Z) / length;
        var distance = MathF.Min(MovePerTick, length - AttackDistance);
        var desiredX = pos.X + dxn * distance;
        var desiredZ = pos.Z + dzn * distance;
        var sideX = -dzn * distance;
        var sideZ = dxn * distance;

        if (TryMove(id, desiredX, desiredZ) ||
            TryMove(id, desiredX + sideX, desiredZ + sideZ) ||
            TryMove(id, desiredX - sideX, desiredZ - sideZ) ||
            TryMove(id, pos.X + sideX, pos.Z + sideZ) ||
            TryMove(id, pos.X - sideX, pos.Z - sideZ))
        {
            ref var p = ref _stores.Positions.GetRef(id);
            p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(dxn, dzn), LookMath.DefaultMaxTurnDegreesPerTick);
        }
    }

    /// <summary>Concrete Spider rule: where to step. Validity itself is shared (<see cref="GroundMobMovement"/>).</summary>
    private bool TryMove(EntityId id, float x, float z)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return false;
        if (!GroundMobMovement.CanStandAt(_world, x, pos.Y, z)) return false;
        ref var p = ref _stores.Positions.GetRef(id);
        p.X = x;
        p.Z = z;
        return true;
    }

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var id in _spiders.Entities)
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
