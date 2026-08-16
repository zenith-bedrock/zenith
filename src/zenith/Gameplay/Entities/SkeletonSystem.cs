using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXII — fifth ECS-authoritative actor, and the first ranged one. Position/Health/
/// ActorIdentity/DespawnTracking live in <see cref="EntityRuntime"/>'s shared component stores;
/// only <see cref="SkeletonState"/> (shot cooldown) is feature-specific and owned here. Skeleton
/// still owns range/target decisions; <see cref="ProjectileSystem"/> remains the sole projectile
/// lifecycle owner — this system only calls <see cref="ProjectileSystem.TrySpawnFromActor"/>, same
/// call it made before migration, now with an ECS-derived owner id instead of a concrete
/// <c>Skeleton.EntityId</c>. This is the ECS-to-ECS composition the ECS foundation was meant to
/// make possible: no <c>SkeletonProjectileComponent</c>, no ability-framework — a direct call
/// between two systems, exactly as narrow as it was before migration.
/// </summary>
sealed class SkeletonSystem : IGameSystem
{
    private const float SpawnDistance = 10f;
    private const float DetectionDistance = 20f;
    private const float MinimumRange = 5f;
    /// <summary>
    /// Phase XXIII — spacing-maintenance thresholds. Skeleton previously never moved at all after
    /// spawn (identified as a real gap: "Skeleton should not behave like Zombie that fires
    /// projectiles" — spacing is part of its identity). No live-client measurement backs these exact
    /// distances (MEDIUM/LOW confidence, see docs/entity-fidelity.md) — they establish the mechanism
    /// (retreat when too close, approach when too far, hold and track otherwise), not a tuned value.
    /// </summary>
    private const float RetreatBelowRange = MinimumRange;
    private const float ApproachAboveRange = MinimumRange * 2f;
    private const float MovePerTick = 0.07f; // Slower than Zombie's chase (0.08) — repositioning, not pursuit.
    private const float AttackDistance = 2.25f;
    private const float AttackDamage = 4f;
    private const float ShotSpeed = 0.45f;
    private const int ShotCooldownTicks = 30;
    private const string LootItemName = "minecraft:bone";
    /// <summary>Vanilla-parity hostile-mob kill reward (Phase XI.4).</summary>
    private const int KillExperience = 5;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<SkeletonState> _skeletons;
    private readonly ProjectileSystem _projectiles;
    private readonly StackId _lootItem;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = [];
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.
    private bool _bootstrapSpawned;

    public SkeletonSystem(World.World world, PlayerManager players, EntityRuntime stores, ProjectileSystem projectiles, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _skeletons = new ComponentStore<SkeletonState>(stores.Entities);
        _projectiles = projectiles;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    internal IReadOnlyList<EntityId> Skeletons => _skeletons.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<SkeletonState> SkeletonStates => _skeletons;

    /// <summary>Cross-species dispatch seam (Phase XXI/XXII) — "is this ECS entity a skeleton," nothing more.</summary>
    internal bool Owns(EntityId id) => _skeletons.Has(id);
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (_skeletons.Count != 0)
            _bootstrapSpawned = true;
        if (!_bootstrapSpawned)
        {
            var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
            if (player is not null)
            {
                SpawnSkeleton(player.PositionX + SpawnDistance, player.PositionY, player.PositionZ);
                _bootstrapSpawned = true;
            }
        }

        _tickScratch.Clear();
        _tickScratch.AddRange(_skeletons.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;

            var target = FindTarget(id, online);
            MaintainRange(id, target);
            ApplyGravity(id);
            ReconcileViewers(id, online);
            TryRangedAttack(id, target, clock, online);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveSkeletonId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    /// <summary>
    /// Phase XXIII — spacing-maintenance: always tracks the target's direction with the head/body
    /// yaw (even when holding position), retreats if the target closes inside
    /// <see cref="RetreatBelowRange"/>, approaches if it's beyond <see cref="ApproachAboveRange"/>,
    /// otherwise holds. This is what makes Skeleton a ranged mob rather than "a Zombie that fires
    /// projectiles" — see the type doc comment.
    /// </summary>
    private void MaintainRange(EntityId id, Player.Player? target)
    {
        if (target is null) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;

        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        var distanceSquared = dx * dx + dz * dz;
        if (distanceSquared <= 0.0001f) return;
        var distance = MathF.Sqrt(distanceSquared);

        var desiredYaw = LookMath.YawTowards(dx, dz);
        ref var p = ref _stores.Positions.GetRef(id);
        p.Yaw = LookMath.MoveYawTowards(p.Yaw, desiredYaw, LookMath.DefaultMaxTurnDegreesPerTick);

        if (distance < RetreatBelowRange)
        {
            var step = MathF.Min(MovePerTick, RetreatBelowRange - distance);
            TryMove(id, pos.X - dx / distance * step, pos.Z - dz / distance * step);
        }
        else if (distance > ApproachAboveRange)
        {
            var step = MathF.Min(MovePerTick, distance - ApproachAboveRange);
            TryMove(id, pos.X + dx / distance * step, pos.Z + dz / distance * step);
        }
    }

    /// <summary>Concrete Skeleton rule: where to step. Validity itself is shared (<see cref="GroundMobMovement"/>).</summary>
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
            foreach (var id in _skeletons.Entities)
            {
                if (!_stores.Identities.TryGet(id, out var identity)) continue;
                var key = (identity.ActorUniqueId, peer.RuntimeId);
                if (!_replicated.Contains(key)) continue;
                if (!_stores.Positions.TryGet(id, out var pos)) continue;
                var current = new ProjectedPose(pos.X, pos.Y, pos.Z, pos.Yaw);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                    continue;
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
            }
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }

    /// <summary>
    /// Range/cooldown-gated shot: the same decision shape as Zombie's/Spider's <c>TryAttackPlayer</c>
    /// — extracted here to match that sibling naming/structure instead of living inline in <see
    /// cref="Tick"/>'s loop body.
    /// </summary>
    private void TryRangedAttack(EntityId id, Player.Player? target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (target is null) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_skeletons.TryGet(id, out var state) || state.NextShotTick > clock.CurrentTick) return;

        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance < MinimumRange || distance < 0.001f) return;

        // Yaw tracking is owned by MaintainRange (runs every tick, including off-cooldown ticks);
        // this only aims the projectile itself.
        if (!_stores.Identities.TryGet(id, out var identity)) return;

        // Ballistic arc, not a fixed vertical launch (Phase XXIII-B fix — see ProjectileSystem's
        // GravityPerTick doc comment for why a flat vertical velocity made every real shot fall
        // short). Solve for the vertical velocity that reaches the target's height in exactly the
        // number of ticks the horizontal velocity takes to cross the horizontal distance:
        // dy = v0*T - 0.5*g*T^2  =>  v0 = dy/T + 0.5*g*T.
        const float launchHeight = 1.2f;
        const float targetHeight = 1.0f; // aim roughly center-of-mass, not the target's feet.
        var launchY = pos.Y + launchHeight;
        var targetY = target.PositionY + targetHeight;
        var travelTicks = MathF.Max(distance / ShotSpeed, 1f);
        var verticalVelocity = (targetY - launchY) / travelTicks + 0.5f * ProjectileSystem.GravityPerTick * travelTicks;

        if (_projectiles.TrySpawnFromActor(identity.ActorUniqueId, pos.X, launchY, pos.Z,
                dx / distance * ShotSpeed, verticalVelocity, dz / distance * ShotSpeed, online))
        {
            ref var trackedState = ref _skeletons.GetRef(id);
            trackedState.NextShotTick = clock.CurrentTick + ShotCooldownTicks;
        }
    }

    private bool IsKnownAliveSkeletonId(long actorUniqueId)
    {
        foreach (var id in _skeletons.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnSkeleton(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Skeleton.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(20f) });
        _stores.Velocities.Set(id, new Velocity()); // Phase XXIX: Y reused as gravity fall-speed.
        _stores.Despawn.Set(id, new DespawnTracking());
        _skeletons.Set(id, new SkeletonState());
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
            }
        _stores.DestroyActor(identity.ActorRuntimeId, id);
        DespawnCount++;
        return true;
    }

    private Player.Player? FindTarget(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return null;
        return online.Where(p => p.IsInGame && !p.IsDead)
            .OrderBy(p => (p.PositionX - pos.X) * (p.PositionX - pos.X) + (p.PositionZ - pos.Z) * (p.PositionZ - pos.Z))
            .FirstOrDefault(p => (p.PositionX - pos.X) * (p.PositionX - pos.X) + (p.PositionZ - pos.Z) * (p.PositionZ - pos.Z) <= DetectionDistance * DetectionDistance);
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
                peer.Session.Protocol.Entity.SendAddSkeleton(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
                peer.Session.Protocol.Entity.SendHealth(identity.ActorRuntimeId, health.Current, health.Maximum);
                _lastProjected[(identity.ActorUniqueId, peer.RuntimeId)] = new ProjectedPose(pos.X, pos.Y, pos.Z, pos.Yaw);
            },
            onExit: peer =>
            {
                _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
            });
    }

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, currentTick, TryApplyDamage);

    /// <summary>Concrete Skeleton health/removal operation shared by the Projectile vertical slice.</summary>
    internal bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Skeleton", currentTick,
            destroyActor: sid =>
            {
                if (_stores.Identities.TryGet(sid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, sid);
            },
            onDeathReplicatedToPeer: peer =>
            {
                if (_stores.Identities.TryGet(id, out var identity))
                    _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
            });
}
