using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

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
            ApplyPlayerAttacks(id, online);
            if (!_stores.Entities.IsAlive(id)) continue;

            var target = FindTarget(id, online);
            ReconcileViewers(id, online);
            TryRangedAttack(id, target, clock, online);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveSkeletonId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
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

        ref var p = ref _stores.Positions.GetRef(id);
        p.Yaw = MathF.Atan2(-dx, dz) * (180f / MathF.PI);
        if (!_stores.Identities.TryGet(id, out var identity)) return;

        if (_projectiles.TrySpawnFromActor(identity.ActorUniqueId, pos.X, pos.Y + 1.2f, pos.Z,
                dx / distance * ShotSpeed, 0.08f, dz / distance * ShotSpeed, online))
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
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
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
            },
            onExit: peer => peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId));
    }

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, TryApplyDamage);

    /// <summary>Concrete Skeleton health/removal operation shared by the Projectile vertical slice.</summary>
    internal bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Skeleton",
            destroyActor: sid =>
            {
                if (_stores.Identities.TryGet(sid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, sid);
            });
}
