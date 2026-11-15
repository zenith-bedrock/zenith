using Zenith.Gameplay.Ports;
using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXI — first ECS-authoritative actor. Position/Health/Velocity/ActorIdentity/
/// DespawnTracking live in <see cref="EntityRuntime"/>'s shared component stores; only
/// <see cref="ZombieState"/> (retained target, attack cooldown) is feature-specific and owned
/// here. The old <c>Zombie</c> object and <c>ZombieStore</c> are gone — there is exactly one
/// authoritative copy of this data now, not a concrete object mirroring what the components
/// already hold. Combat/loot/XP bookkeeping moved to <see cref="DamageableActorCombat"/> (shared by every species). See docs/history/phases/phase-xxi-ecs-foundation-findings.md.
/// </summary>
sealed class ZombieSystem : IGameSystem
{
    private const float SpawnDistance = 4f;
    private const float DetectionDistance = 24f;
    private float AttackDistance => _mob.Definition.Melee!.Reach;
    private const float MovePerTick = 0.08f;
    // Phase XVI — world lifecycle: despawn if unseen. Larger than DetectionDistance so an actively
    // chasing zombie is never mid-engagement when this fires.
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<ZombieState> _zombies;
    private readonly MobCore _mob;
    private readonly GroundMobBody _body;
    private readonly ActorReplication _replication;
    // Phase XXI addendum 2: reused every tick instead of a fresh `new EntityId[]` snapshot —
    // structural mutation (despawn/death) during iteration still needs a stable list to walk, but
    // it doesn't need a new allocation to provide one.
    private readonly List<EntityId> _tickScratch = [];

    public ZombieSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Zombie, world, players, stores, itemPalette);
        _replication = _mob.Replication;
        _body = new GroundMobBody(world, stores, players);
        _zombies = new ComponentStore<ZombieState>(stores.Entities);
    }

    internal IReadOnlyList<EntityId> Zombies => _zombies.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<ZombieState> ZombieStates => _zombies;

    /// <summary>Cross-species dispatch seam (Phase XXI, Projectile's generalized hit query) — "is this ECS entity a zombie," nothing more.</summary>
    internal bool Owns(EntityId id) => _zombies.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedMoveSkippedCount => _replication.MoveSkippedCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _replication.BeginTick(online);
        if (online.Count == 0) return;

        _tickScratch.Clear();
        _tickScratch.AddRange(_zombies.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;
            var target = _body.FindOrAcquireTarget(id, online, ref _zombies.GetRef(id).TargetPlayerRuntimeId, DetectionDistance);
            if (target is not null)
                _body.ChaseTarget(id, target, DetectionDistance, AttackDistance, MovePerTick);
            TryAttackPlayer(id, target, clock, online);
            _body.ApplyGravity(id);
            ReconcileViewers(id, online);
        }

        _replication.Prune(_zombies.Entities, online);
        _replication.ReplicateMoves(_zombies.Entities, online);
    }


    /// <summary>
    /// The feature-specific composition step every migrated actor needs on top of
    /// <see cref="EntityRuntime.CreateActor"/>: this is where "what makes it a zombie" gets
    /// attached, in one obvious place, instead of a scattered list of store registrations at every
    /// call site.
    /// </summary>
    internal EntityId SpawnZombie(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        _zombies.Set(id, new ZombieState());
        return id;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online) =>
        _replication.TryDespawn(id, clock, online, DespawnRadius);

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online) =>
        _replication.Reconcile(id, online);

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.ApplyPlayerAttacks(id, online, currentTick, TryApplyDamage);

    private void TryAttackPlayer(EntityId id, Player.Player? target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (target is null || !_mob.IsMeleeReady(id, clock.CurrentTick)) return;
        if (!_stores.Positions.TryGet(id, out var pos) || !GroundMobBody.IsTargetValid(pos, target, DetectionDistance, out _)) return;
        _mob.TryMeleeAttack(id, target, clock.CurrentTick, online);
    }

    /// <summary>Concrete Zombie health/removal operation shared by the Projectile vertical slice.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.TryApplyDamage(id, source, amount, online, currentTick);






}
