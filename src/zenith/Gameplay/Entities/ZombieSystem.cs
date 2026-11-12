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
/// already hold. Combat/loot/XP bookkeeping moved to <see cref="DamageableActorCombat"/> (the
/// ECS-native sibling of <see cref="GroundMobCombat"/>, which still serves every unmigrated
/// mob). See docs/history/phases/phase-xxi-ecs-foundation-findings.md.
/// </summary>
sealed class ZombieSystem : IGameSystem
{
    private const float SpawnDistance = 4f;
    private const float DetectionDistance = 24f;
    private const float AttackDistance = 2.25f;
    private const float MovePerTick = 0.08f;
    private const float AttackDamage = 4f;
    private const int AttackCooldownTicks = 20;
    // Phase XVI — knockback: a movement consequence of combat, deliberately not part of
    // GroundMobCombat/DamageableActorCombat (which never touch position) or a shared primitive
    // (single mob so far).
    private const float KnockbackImpulse = 0.3f;
    private const float KnockbackDecayPerTick = 0.5f;
    private const float KnockbackNegligible = 0.001f;
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
    private bool _bootstrapSpawned;

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
        if (online.Count == 0) return;
        if (_zombies.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapZombie(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_zombies.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;
            ApplyKnockbackMotion(id);
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


    private void EnsureBootstrapZombie(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _zombies.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnDistance;
        var z = player.PositionZ;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        SpawnZombie(x, y, z);
        _bootstrapSpawned = true;
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
        _replication.Reconcile(id, online,
            (peer, identity, pos) => peer.Session.Protocol.Entity.SendAddZombie(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw));

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, currentTick, TryApplyDamageAndKnockback);

    /// <summary>
    /// Wraps the shared kill bookkeeping with a purely local, Zombie-only side effect: a landed hit
    /// also pushes the zombie back. Neither GroundMobCombat's nor DamageableActorCombat's contract
    /// needed to grow to support this.
    /// </summary>
    private bool TryApplyDamageAndKnockback(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        var applied = TryApplyDamage(id, source, amount, online, currentTick);
        if (applied && _stores.Entities.IsAlive(id) && source.OwnerRuntimeId is { } attackerId)
        {
            var attacker = _players.GetByRuntimeId(attackerId);
            if (attacker is not null)
                ApplyKnockbackImpulse(id, attacker.PositionX, attacker.PositionZ);
        }
        return applied;
    }

    private void ApplyKnockbackImpulse(EntityId id, float fromX, float fromZ)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var dx = pos.X - fromX;
        var dz = pos.Z - fromZ;
        var lengthSquared = dx * dx + dz * dz;
        float nx, nz;
        if (lengthSquared > 0.0001f)
        {
            var length = MathF.Sqrt(lengthSquared);
            nx = dx / length;
            nz = dz / length;
        }
        else
        {
            nx = 0f;
            nz = 1f;
        }

        ref var vel = ref _stores.Velocities.GetRef(id);
        vel.X = nx * KnockbackImpulse;
        vel.Z = nz * KnockbackImpulse;
    }

    private void ApplyKnockbackMotion(EntityId id)
    {
        if (!_stores.Velocities.TryGet(id, out var vel)) return;
        if (vel.X == 0f && vel.Z == 0f) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;

        var x = pos.X + vel.X;
        var z = pos.Z + vel.Z;
        if (GroundMobMovement.TryMoveHorizontal(_world, pos.X, pos.Y, pos.Z, x, z, out var resolvedY))
        {
            ref var p = ref _stores.Positions.GetRef(id);
            p.X = x;
            p.Y = resolvedY;
            p.Z = z;
        }

        ref var v = ref _stores.Velocities.GetRef(id);
        v.X *= KnockbackDecayPerTick;
        v.Z *= KnockbackDecayPerTick;
        if (MathF.Abs(v.X) < KnockbackNegligible) v.X = 0f;
        if (MathF.Abs(v.Z) < KnockbackNegligible) v.Z = 0f;
    }

    private void TryAttackPlayer(EntityId id, Player.Player? target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _zombies.GetRef(id);
        if (state.NextAttackTick > clock.CurrentTick) return;
        if (target is null) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!GroundMobBody.IsTargetValid(pos, target, DetectionDistance, out var distanceSquared) || distanceSquared > AttackDistance * AttackDistance)
            return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        var result = PlayerDamage.ApplyCore(target, _players, DamageSource.MeleeFrom(identity.ActorUniqueId), AttackDamage,
            clock.CurrentTick, target.PositionX - pos.X, target.PositionZ - pos.Z);
        result.Conclude(online);
        if (result.Applied)
        {
            state.NextAttackTick = clock.CurrentTick + AttackCooldownTicks;
            foreach (var peer in online)
                if (_replication.Viewers.Contains((identity.ActorUniqueId, peer.RuntimeId)))
                    peer.Session.Protocol.Entity.SendAttackSwing(identity.ActorRuntimeId);
        }
    }

    /// <summary>Concrete Zombie health/removal operation shared by the Projectile vertical slice.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        _mob.TryApplyDamage(id, source, amount, online, currentTick);






}
