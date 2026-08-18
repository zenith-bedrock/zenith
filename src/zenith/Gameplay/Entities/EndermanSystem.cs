using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Second "fundamentally different behavior" ground mob (Phase XV). Combat/loot/XP bookkeeping for
/// a player-caused kill is shared via <see cref="DamageableActorCombat"/>, same as every other
/// migrated mob. Movement is teleportation, never <see cref="ZombieSystem"/>'s incremental chase
/// step, and aggression is damage-triggered (attacked → hostile for a while) rather than
/// proximity-triggered (Zombie) or ignite-range-triggered (Creeper). Both are entirely local to
/// this system.
///
/// ECS-authoritative since this migration (see docs/decisions.md, the ADR after §131): Position/
/// Health/ActorIdentity/DespawnTracking live in the shared <see cref="EntityRuntime"/> stores; only
/// <see cref="EndermanState"/> (aggro target/window, teleport/attack cooldowns) is feature-specific
/// and owned here — same shape as <see cref="SpiderState"/>. Enderman never used
/// <see cref="GroundMobMovement.TryMoveHorizontal"/> (teleportation has no path to walk), so it does
/// not use <c>Velocity.Y</c> for gravity either — every landing spot is validated directly via
/// <see cref="GroundMobMovement.IsSupportedGroundCell"/>, unchanged by this migration.
/// </summary>
sealed class EndermanSystem : IGameSystem
{
    private const float SpawnDistance = 5f;
    private const float AttackDistance = 2.25f;
    private const float AttackDamage = 7f;
    private const int AttackCooldownTicks = 20;
    private const int AggroDurationTicks = 100; // 5s @ 20 TPS since the last hit landed.
    private const int MinPassiveTeleportTicks = 60;
    private const int MaxPassiveTeleportTicks = 140;
    private const int AggroTeleportCooldownTicks = 20;
    private const float PassiveTeleportRadius = 8f;
    private const float AggroTeleportRadius = 2f; // Lands just outside melee reach of the target.
    private const int TeleportCandidateAttempts = 6;
    private const string LootItemName = "minecraft:ender_pearl";
    /// <summary>Vanilla-parity hostile-mob kill reward (Phase XI.4).</summary>
    private const int KillExperience = 5;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<EndermanState> _endermen;
    private readonly StackId _lootItem;
    private readonly Random _random;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = new();
    private readonly List<EntityId> _tickScratch = [];
    private bool _bootstrapSpawned;

    public EndermanSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _endermen = new ComponentStore<EndermanState>(stores.Entities);
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Endermen => _endermen.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<EndermanState> EndermanStates => _endermen;

    /// <summary>Cross-species dispatch seam (Phase XXII) — "is this ECS entity an enderman," nothing more.</summary>
    internal bool Owns(EntityId id) => _endermen.Has(id);
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long TeleportCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_endermen.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapEnderman(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_endermen.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;

            ref var state = ref _endermen.GetRef(id);
            if (state.AggroTicksRemaining > 0)
                state.AggroTicksRemaining--;

            if (state.AggroTicksRemaining > 0)
            {
                TickAggro(id, clock, online);
            }
            else
            {
                ref var cleared = ref _endermen.GetRef(id);
                cleared.AggroTargetRuntimeId = null;
                TickPassiveTeleport(id, clock, online);
            }

            ReconcileViewers(id, online);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveEndermanId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    private bool IsKnownAliveEndermanId(long actorUniqueId)
    {
        foreach (var id in _endermen.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    private void EnsureBootstrapEnderman(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _endermen.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX;
        var z = player.PositionZ + SpawnDistance;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        SpawnEnderman(x, y, z);
        _bootstrapSpawned = true;
    }

    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnEnderman(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Enderman.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(20f) }); // Simplified from vanilla's 40 for parity with the other slices.
        _stores.Despawn.Set(id, new DespawnTracking()); // No Velocity: teleportation has no path to integrate, unlike every gravity-bound ground mob.
        _endermen.Set(id, new EndermanState());
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
                peer.Session.Protocol.Entity.SendAddEnderman(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
                peer.Session.Protocol.Entity.SendHealth(identity.ActorRuntimeId, health.Current, health.Maximum);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
                ReplicatedRemovalCount++;
            });
    }

    /// <summary>A landed player hit both damages the Enderman and provokes it — the one place aggro is triggered.</summary>
    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, currentTick,
            (eid, source, amount, peers, tick) => TryApplyDamageAndProvoke(eid, source, amount, peers, tick));

    private bool TryApplyDamageAndProvoke(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        var applied = TryApplyDamage(id, source, amount, online, currentTick);
        if (applied && _stores.Entities.IsAlive(id) && source.OwnerRuntimeId is { } attackerId)
        {
            ref var state = ref _endermen.GetRef(id);
            state.AggroTargetRuntimeId = attackerId;
            state.AggroTicksRemaining = AggroDurationTicks;
        }
        return applied;
    }

    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Enderman", currentTick,
            destroyActor: eid =>
            {
                if (_stores.Identities.TryGet(eid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, eid);
            },
            onDeathReplicatedToPeer: _ => ReplicatedRemovalCount++);

    /// <summary>Neutral state: no target, just an occasional random short teleport.</summary>
    private void TickPassiveTeleport(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _endermen.GetRef(id);
        if (clock.CurrentTick < state.NextPassiveTeleportTick) return;
        state.NextPassiveTeleportTick =
            clock.CurrentTick + (ulong)_random.Next(MinPassiveTeleportTicks, MaxPassiveTeleportTicks);

        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var angle = _random.NextSingle() * MathF.PI * 2f;
        var distance = _random.NextSingle() * PassiveTeleportRadius;
        TryTeleport(id, pos.X + MathF.Cos(angle) * distance, pos.Z + MathF.Sin(angle) * distance, online);
    }

    /// <summary>Hostile state: teleport toward the last attacker and swing once in reach, on cooldown.</summary>
    private void TickAggro(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _endermen.GetRef(id);
        var target = state.AggroTargetRuntimeId is { } targetId ? _players.GetByRuntimeId(targetId) : null;
        if (target is null || !target.IsInGame || target.IsDead)
        {
            state.AggroTicksRemaining = 0;
            state.AggroTargetRuntimeId = null;
            return;
        }

        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        var distanceSquared = dx * dx + dz * dz;

        // Phase XXVI — Enderman previously never set Yaw at all (entity-fidelity audit finding):
        // teleporting toward/attacking a target didn't orient the actor to face them. Snap-facing on
        // every aggro tick matches teleport's own instant, non-gradual movement model — no smoothed
        // turn like GroundMobMovement's walking chase needs.
        ref var p = ref _stores.Positions.GetRef(id);
        p.Yaw = LookMath.YawTowards(dx, dz);

        if (distanceSquared > AttackDistance * AttackDistance && clock.CurrentTick >= state.NextAttackTick)
        {
            ref var s = ref _endermen.GetRef(id);
            s.NextAttackTick = clock.CurrentTick + (ulong)AggroTeleportCooldownTicks;
            var angle = _random.NextSingle() * MathF.PI * 2f;
            TryTeleport(
                id,
                target.PositionX + MathF.Cos(angle) * AggroTeleportRadius,
                target.PositionZ + MathF.Sin(angle) * AggroTeleportRadius,
                online);
            return;
        }

        if (distanceSquared <= AttackDistance * AttackDistance && clock.CurrentTick >= state.NextAttackTick)
        {
            if (!_stores.Identities.TryGet(id, out var identity)) return;
            var result = PlayerDamage.ApplyCore(target, _players, DamageSource.MeleeFrom(identity.ActorUniqueId), AttackDamage, clock.CurrentTick, dx, dz);
            result.Conclude(online);
            if (result.Applied)
            {
                ref var s = ref _endermen.GetRef(id);
                s.NextAttackTick = clock.CurrentTick + AttackCooldownTicks;
            }
        }
    }

    /// <summary>
    /// Reuses GroundMobMovement's position-validity check for the *destination*, not for a walked
    /// step — teleportation has no path, only a landing spot that must be safe to stand on.
    /// </summary>
    private bool TryTeleport(EntityId id, float x, float z, IReadOnlyList<Player.Player> online)
    {
        for (var attempt = 0; attempt < TeleportCandidateAttempts; attempt++)
        {
            var candidateY = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
            if (GroundMobMovement.IsSupportedGroundCell(_world, x, candidateY, z))
            {
                ref var p = ref _stores.Positions.GetRef(id);
                p.X = x;
                p.Y = candidateY;
                p.Z = z;
                TeleportCount++;
                BroadcastTeleportEffect(id, online);
                return true;
            }

            // Jitter and retry — a teleport with no valid landing spot this tick is simply skipped.
            x += (_random.NextSingle() - 0.5f) * 2f;
            z += (_random.NextSingle() - 0.5f) * 2f;
        }

        return false;
    }

    private void BroadcastTeleportEffect(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Identities.TryGet(id, out var identity)) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            if (!_replicated.Contains((identity.ActorUniqueId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, flags: 0, yaw: pos.Yaw, headYaw: pos.Yaw);
            peer.Session.Protocol.World.SendLevelSoundEvent("mob.endermen.portal", pos.X, pos.Y, pos.Z);
        }
    }
}
