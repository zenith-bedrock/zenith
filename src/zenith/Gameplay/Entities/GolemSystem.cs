using Zenith.Gameplay.Ports;
using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XVIII, Priority 4 — a boss-shaped pressure test on "does behavior complexity exceed what
/// one concrete System can reasonably own." It does not: phase transition, melee, and the area slam
/// are three straightforward concrete blocks in this one file, same shape as every other mob
/// system. No component system was needed beyond the one feature-specific <see cref="GolemState"/>.
/// See docs/history/phases/phase-xviii-runtime-pressure-findings.md.
///
/// Melee/loot/XP bookkeeping reuses <see cref="DamageableActorCombat"/> unmodified. The area slam
/// (<see cref="TrySlam"/>) is the first mob ability to damage more than one player in a single
/// action — it turned out to need nothing new: <see cref="PlayerDamage.ApplyCore"/> is already
/// per-player, so looping over everyone in range was sufficient. Player-side knockback was
/// deliberately NOT added: player position is client-authoritative everywhere else in this
/// codebase (see docs/entities.md's capability matrix), and inventing server-pushed player
/// movement for one ability would be exactly the kind of speculative mechanism this project avoids
/// building ahead of a second real need.
///
/// ECS-authoritative since this migration (see docs/decisions.md, the ADR after §131): Position/
/// Health/ActorIdentity/DespawnTracking live in the shared <see cref="EntityRuntime"/> stores,
/// Velocity.Y is reused as gravity fall-speed (Phase XXIX convention), and only
/// <see cref="GolemState"/> (enrage/target/cooldowns) is feature-specific. The boss-phase branching
/// and multi-target slam needed no new ECS primitive — both already operated on world-space
/// coordinates and <see cref="PlayerDamage"/>, independent of what backs Golem's own storage.
/// </summary>
sealed class GolemSystem : IGameSystem
{
    private const float SpawnDistance = 6f;
    private const float DetectionDistance = 20f;
    private const float AttackDistance = 2.5f;
    private const float MovePerTick = 0.07f;
    private const float AttackDamage = 6f;
    private const int AttackCooldownTicks = 20;
    private const float EnrageHealthFraction = 0.5f;
    private const int SlamCooldownTicks = 100; // 5s @ 20 TPS.
    private const float SlamRadius = 4f;
    private const float SlamDamage = 8f;
    /// <summary>
    /// Phase XXIII-B — vanilla parity: a naturally-spawned Iron Golem is passive until a player
    /// attacks it directly or attacks a villager it can "see" (Zenith has no village/reputation
    /// system, so proximity + a short recency window stands in for that — see
    /// <c>Player.LastVillagerAttack</c>'s doc comment). Confirmed against the Minecraft Wiki: golems
    /// retaliate against their own attacker, and become hostile toward a player who damages a
    /// villager near them.
    /// </summary>
    private const int ProvokeWindowTicks = 200; // 10s @ 20 TPS.
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<GolemState> _golems;
    private readonly MobCore _mob;
    private readonly GroundMobBody _body;
    private readonly ActorReplication _replication;
    private readonly List<EntityId> _tickScratch = [];
    private ulong _currentTick;

    public GolemSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Golem, world, players, stores, itemPalette);
        _replication = _mob.Replication;
        _body = new GroundMobBody(world, stores, players);
        _golems = new ComponentStore<GolemState>(stores.Entities);
    }

    internal IReadOnlyList<EntityId> Golems => _golems.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<GolemState> GolemStates => _golems;

    /// <summary>Cross-species dispatch seam (Phase XXII) — "is this ECS entity a golem," nothing more.</summary>
    internal bool Owns(EntityId id) => _golems.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedMoveSkippedCount => _replication.MoveSkippedCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;
    internal long SlamCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _replication.BeginTick(online);
        _currentTick = clock.CurrentTick;
        if (online.Count == 0) return;

        _tickScratch.Clear();
        _tickScratch.AddRange(_golems.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online);
            if (!_stores.Entities.IsAlive(id)) continue;

            if (!_stores.Health.TryGet(id, out var healthComponent)) continue;
            ref var state = ref _golems.GetRef(id);
            state.IsEnraged = healthComponent.State.Current <= healthComponent.State.Maximum * EnrageHealthFraction;

            var target = FindProvokedTarget(id, online);
            if (target is not null)
            {
                AdvanceTowardTarget(id, target);
                TryAttackPlayer(id, target, clock, online);
            }
            if (state.IsEnraged)
                TrySlam(id, online, clock);

            _body.ApplyGravity(id);
            ReconcileViewers(id, online);
        }

        _replication.Prune(_golems.Entities, online);
        _replication.ReplicateMoves(_golems.Entities, online);
    }


    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnGolem(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        _golems.Set(id, new GolemState());
        return id;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online) =>
        _replication.TryDespawn(id, clock, online, DespawnRadius);

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online) =>
        _replication.Reconcile(id, online);

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, _currentTick, TryApplyDamage);

    /// <summary>Concrete Golem health/removal operation — same shape as every other migrated actor's. A player attacker provokes retaliation (self-defense — vanilla golems always fight back).</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        if (source.OwnerRuntimeId is { } attackerId && _golems.Has(id))
        {
            ref var state = ref _golems.GetRef(id);
            state.TargetPlayerRuntimeId = attackerId;
        }

        return _mob.TryApplyDamage(id, source, amount, online, currentTick);
    }

    /// <summary>
    /// Phase XXIII-B — replaces the old "always attack the nearest player" boss behavior: a golem
    /// stays passive until provoked (see <see cref="ProvokeWindowTicks"/>'s doc comment), then
    /// retains that specific player as its target the same way Zombie/Spider retain theirs.
    /// </summary>
    private Player.Player? FindProvokedTarget(EntityId id, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _golems.GetRef(id);
        if (!_stores.Positions.TryGet(id, out var pos)) return null;

        if (state.TargetPlayerRuntimeId is { } retainedId)
        {
            var retained = _players.GetByRuntimeId(retainedId);
            if (retained is not null && IsTargetValid(pos, retained))
                return retained;
            state.TargetPlayerRuntimeId = null;
        }

        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            if (player.LastVillagerAttack is not { } attack) continue;
            if (_currentTick - attack.Tick > ProvokeWindowTicks) continue;

            var dx = attack.X - pos.X;
            var dz = attack.Z - pos.Z;
            if (dx * dx + dz * dz > DetectionDistance * DetectionDistance) continue;

            state.TargetPlayerRuntimeId = player.RuntimeId;
            return player;
        }

        return null;
    }

    private static bool IsTargetValid(Position pos, Player.Player target)
    {
        if (!target.IsInGame || target.IsDead) return false;
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        return dx * dx + dz * dz <= DetectionDistance * DetectionDistance;
    }

    private void AdvanceTowardTarget(EntityId id, Player.Player target)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        var distanceSquared = dx * dx + dz * dz;
        if (distanceSquared <= AttackDistance * AttackDistance || distanceSquared <= 0.0001f) return;

        var length = MathF.Sqrt(distanceSquared);
        var dxn = dx / length;
        var dzn = dz / length;
        var distance = MathF.Min(MovePerTick, length - AttackDistance);
        if (_body.TryMove(id, pos.X + dxn * distance, pos.Z + dzn * distance))
        {
            ref var p = ref _stores.Positions.GetRef(id);
            p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(dxn, dzn), LookMath.DefaultMaxTurnDegreesPerTick);
        }
    }



    private void TryAttackPlayer(EntityId id, Player.Player target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _golems.GetRef(id);
        if (clock.CurrentTick < state.NextAttackTick) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        if (dx * dx + dz * dz > AttackDistance * AttackDistance) return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        var result = PlayerDamage.ApplyCore(target, _players, DamageSource.MeleeFrom(identity.ActorUniqueId), AttackDamage, clock.CurrentTick, dx, dz);
        result.Conclude(online);
        if (result.Applied)
        {
            ref var s = ref _golems.GetRef(id);
            s.NextAttackTick = clock.CurrentTick + AttackCooldownTicks;
            foreach (var peer in online)
                if (_replication.Viewers.Contains((identity.ActorUniqueId, peer.RuntimeId)))
                    peer.Client.Entity.SendAttackSwing(identity.ActorRuntimeId);
        }
    }

    /// <summary>
    /// Area ability, only reachable once enraged: damages every player within <see cref="SlamRadius"/>
    /// in one action. The first mob ability to hit more than one player at once — turned out to need
    /// no new primitive, just a loop over <see cref="PlayerDamage.ApplyCore"/>, which was already
    /// per-player and had no assumption baked in that only one player could be hit per call.
    /// </summary>
    private void TrySlam(EntityId id, IReadOnlyList<Player.Player> online, GameClock clock)
    {
        ref var state = ref _golems.GetRef(id);
        if (clock.CurrentTick < state.NextSlamTick) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;

        var radiusSquared = SlamRadius * SlamRadius;
        var hitAny = false;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = player.PositionX - pos.X;
            var dz = player.PositionZ - pos.Z;
            if (dx * dx + dz * dz > radiusSquared) continue;
            var result = PlayerDamage.ApplyCore(player, _players, DamageSource.MeleeFrom(identity.ActorUniqueId), SlamDamage, clock.CurrentTick, dx, dz);
            result.Conclude(online);
            if (result.Applied)
                hitAny = true;
        }

        if (!hitAny) return;
        ref var s = ref _golems.GetRef(id);
        s.NextSlamTick = clock.CurrentTick + SlamCooldownTicks;
        SlamCount++;
        foreach (var peer in online)
            if (peer.IsInGame)
                peer.Client.World.SendLevelSoundEvent("mob.irongolem.attack", pos.X, pos.Y, pos.Z);
    }

}
