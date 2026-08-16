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
    private const string LootItemName = "minecraft:rotten_flesh";
    /// <summary>Vanilla-parity hostile-mob kill reward (Phase XI.4).</summary>
    private const int KillExperience = 5;
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
    private readonly StackId _lootItem;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
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
        _zombies = new ComponentStore<ZombieState>(stores.Entities);
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    internal IReadOnlyList<EntityId> Zombies => _zombies.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<ZombieState> ZombieStates => _zombies;

    /// <summary>Cross-species dispatch seam (Phase XXI, Projectile's generalized hit query) — "is this ECS entity a zombie," nothing more.</summary>
    internal bool Owns(EntityId id) => _zombies.Has(id);
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }

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
            var target = FindOrAcquireTarget(id, online);
            if (target is not null)
                AdvanceTowardTarget(id, target);
            TryAttackPlayer(id, target, clock, online);
            ApplyGravity(id);
            ReconcileViewers(id, online);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveZombieId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private bool IsKnownAliveZombieId(long actorUniqueId)
    {
        foreach (var id in _zombies.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
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
        var actorUniqueId = _players.AllocateRuntimeId();
        // CreateActor only returns null if AllocateRuntimeId ever produced a runtime id already in
        // use, which it never does by construction — see EntityRuntimeTests for the rollback path
        // this guards, proven in isolation rather than trusted here.
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Zombie.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(20f) });
        _stores.Velocities.Set(id, new Velocity());
        _stores.Despawn.Set(id, new DespawnTracking());
        _zombies.Set(id, new ZombieState());
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
                peer.Session.Protocol.Entity.SendAddZombie(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
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
            var attacker = online.FirstOrDefault(p => p.RuntimeId == attackerId);
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
        if (!IsTargetValid(pos, target, out var distanceSquared) || distanceSquared > AttackDistance * AttackDistance)
            return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        if (PlayerDamage.Apply(target, _players, online, DamageSource.MeleeFrom(identity.ActorUniqueId), AttackDamage,
                clock.CurrentTick, target.PositionX - pos.X, target.PositionZ - pos.Z))
        {
            state.NextAttackTick = clock.CurrentTick + AttackCooldownTicks;
            foreach (var peer in online)
                if (_replicated.Contains((identity.ActorUniqueId, peer.RuntimeId)))
                    peer.Session.Protocol.Entity.SendAttackSwing(identity.ActorRuntimeId);
        }
    }

    /// <summary>Concrete Zombie health/removal operation shared by the Projectile vertical slice.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Zombie", currentTick,
            destroyActor: zid =>
            {
                if (_stores.Identities.TryGet(zid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, zid);
            },
            onDeathReplicatedToPeer: peer =>
            {
                if (_stores.Identities.TryGet(id, out var identity))
                    _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                ReplicatedRemovalCount++;
            });

    private Player.Player? FindOrAcquireTarget(EntityId id, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _zombies.GetRef(id);
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

    /// <summary>Concrete Zombie rule: where to step. Validity itself is shared (<see cref="GroundMobMovement"/>, Phase XV).</summary>
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

    /// <summary>
    /// Phase XXIX: one tick of gravity/falling/landing, reusing <see cref="Velocity.Y"/> as a
    /// downward fall-speed magnitude (same field Zombie's own knockback already writes X/Z of).
    /// </summary>
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
            foreach (var id in _zombies.Entities)
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
