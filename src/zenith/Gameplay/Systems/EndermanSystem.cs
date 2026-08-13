using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Second "fundamentally different behavior" ground mob (Phase XV). Combat/loot/XP bookkeeping for
/// a player-caused kill is shared via <see cref="GroundMobCombat"/>, same as every other ground mob.
/// Movement is teleportation, never <see cref="ZombieSystem"/>'s incremental chase step, and
/// aggression is damage-triggered (attacked → hostile for a while) rather than proximity-triggered
/// (Zombie) or ignite-range-triggered (Creeper). Both are entirely local to this system.
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
    private readonly EndermanStore _endermen;
    private readonly StackId _lootItem;
    private readonly Random _random;
    private readonly HashSet<(long EndermanId, long PlayerId)> _replicated = new();
    private bool _bootstrapSpawned;

    public EndermanSystem(World.World world, PlayerManager players, EndermanStore endermen, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _endermen = endermen;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _random = random ?? new Random();
    }

    public EndermanStore Endermen => _endermen;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long TeleportCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_endermen.Active.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapEnderman(online);

        foreach (var enderman in _endermen.Active.ToArray())
        {
            if (!enderman.IsActive) continue;
            if (TryDespawn(enderman, clock, online)) continue;
            ReconcileViewers(enderman, online);
            ApplyPlayerAttacks(enderman, online);
            if (!enderman.IsActive) continue;

            if (enderman.AggroTicksRemaining > 0)
                enderman.AggroTicksRemaining--;

            if (enderman.AggroTicksRemaining > 0)
            {
                TickAggro(enderman, clock, online);
            }
            else
            {
                enderman.AggroTargetRuntimeId = null;
                TickPassiveTeleport(enderman, clock, online);
            }

            ReconcileViewers(enderman, online);
        }

        _replicated.RemoveWhere(pair => !_endermen.Active.Any(e => e.EntityId == pair.EndermanId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    private void EnsureBootstrapEnderman(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _endermen.Active.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX;
        var z = player.PositionZ + SpawnDistance;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        var entityId = _players.AllocateRuntimeId();
        if (_endermen.TryAdd(new Enderman(entityId, (ulong)entityId, x, y, z)))
            _bootstrapSpawned = true;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(Enderman enderman, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            enderman.PositionX, enderman.PositionZ, online, DespawnRadius, clock.CurrentTick, enderman.LastSeenNearPlayerTick);
        enderman.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        foreach (var peer in online)
            if (_replicated.Remove((enderman.EntityId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendRemoveActor(enderman.EntityId);
        enderman.Remove();
        _endermen.Remove(enderman);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(Enderman enderman, IReadOnlyList<Player.Player> online) =>
        ViewerReconciliation.Sync(
            enderman.EntityId, online, _replicated,
            peer => ActorInterest.Includes(peer, enderman.PositionX, enderman.PositionZ),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddEnderman(
                    enderman.EntityId, enderman.RuntimeId, enderman.PositionX, enderman.PositionY, enderman.PositionZ, enderman.Yaw);
                peer.Session.Protocol.Entity.SendHealth(enderman.RuntimeId, enderman.Health.Current, enderman.Health.Maximum);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                peer.Session.Protocol.Entity.SendRemoveActor(enderman.EntityId);
                ReplicatedRemovalCount++;
            });

    /// <summary>A landed player hit both damages the Enderman and provokes it — the one place aggro is triggered.</summary>
    private void ApplyPlayerAttacks(Enderman enderman, IReadOnlyList<Player.Player> online) =>
        GroundMobCombat.ApplyPlayerMeleeAttacks(enderman, online, AttackDistance, AttackDamage, TryApplyDamageAndProvoke);

    private bool TryApplyDamageAndProvoke(Enderman enderman, DamageSource source, float amount, IReadOnlyList<Player.Player> online)
    {
        var applied = TryApplyDamage(enderman, source, amount, online);
        if (applied && enderman.IsActive && source.OwnerRuntimeId is { } attackerId)
        {
            enderman.AggroTargetRuntimeId = attackerId;
            enderman.AggroTicksRemaining = AggroDurationTicks;
        }
        return applied;
    }

    public bool TryApplyDamage(Enderman enderman, DamageSource source, float amount, IReadOnlyList<Player.Player> online) =>
        GroundMobCombat.TryApplyDamage(
            enderman, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Enderman",
            removeFromStore: _endermen.Remove,
            onDeathReplicatedToPeer: _ => ReplicatedRemovalCount++);

    /// <summary>Neutral state: no target, just an occasional random short teleport.</summary>
    private void TickPassiveTeleport(Enderman enderman, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick < enderman.NextPassiveTeleportTick) return;
        enderman.NextPassiveTeleportTick =
            clock.CurrentTick + (ulong)_random.Next(MinPassiveTeleportTicks, MaxPassiveTeleportTicks);

        var angle = _random.NextSingle() * MathF.PI * 2f;
        var distance = _random.NextSingle() * PassiveTeleportRadius;
        TryTeleport(enderman, enderman.PositionX + MathF.Cos(angle) * distance, enderman.PositionZ + MathF.Sin(angle) * distance, online);
    }

    /// <summary>Hostile state: teleport toward the last attacker and swing once in reach, on cooldown.</summary>
    private void TickAggro(Enderman enderman, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var target = enderman.AggroTargetRuntimeId is { } id ? online.FirstOrDefault(p => p.RuntimeId == id) : null;
        if (target is null || !target.IsInGame || target.IsDead)
        {
            enderman.AggroTicksRemaining = 0;
            enderman.AggroTargetRuntimeId = null;
            return;
        }

        var dx = target.PositionX - enderman.PositionX;
        var dz = target.PositionZ - enderman.PositionZ;
        var distanceSquared = dx * dx + dz * dz;

        if (distanceSquared > AttackDistance * AttackDistance && clock.CurrentTick >= enderman.NextAttackTick)
        {
            enderman.NextAttackTick = clock.CurrentTick + (ulong)AggroTeleportCooldownTicks;
            var angle = _random.NextSingle() * MathF.PI * 2f;
            TryTeleport(
                enderman,
                target.PositionX + MathF.Cos(angle) * AggroTeleportRadius,
                target.PositionZ + MathF.Sin(angle) * AggroTeleportRadius,
                online);
            return;
        }

        if (distanceSquared <= AttackDistance * AttackDistance && clock.CurrentTick >= enderman.NextAttackTick)
        {
            if (PlayerDamage.Apply(target, _players, online, DamageSource.MeleeFrom(enderman.EntityId), AttackDamage))
                enderman.NextAttackTick = clock.CurrentTick + AttackCooldownTicks;
        }
    }

    /// <summary>
    /// Reuses GroundMobMovement's position-validity check for the *destination*, not for a walked
    /// step — teleportation has no path, only a landing spot that must be safe to stand on.
    /// </summary>
    private bool TryTeleport(Enderman enderman, float x, float z, IReadOnlyList<Player.Player> online)
    {
        for (var attempt = 0; attempt < TeleportCandidateAttempts; attempt++)
        {
            var candidateY = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
            if (GroundMobMovement.CanStandAt(_world, x, candidateY, z))
            {
                enderman.PositionX = x;
                enderman.PositionY = candidateY;
                enderman.PositionZ = z;
                TeleportCount++;
                BroadcastTeleportEffect(enderman, online);
                return true;
            }

            // Jitter and retry — a teleport with no valid landing spot this tick is simply skipped.
            x += (_random.NextSingle() - 0.5f) * 2f;
            z += (_random.NextSingle() - 0.5f) * 2f;
        }

        return false;
    }

    private void BroadcastTeleportEffect(Enderman enderman, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            if (!_replicated.Contains((enderman.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw((ulong)enderman.EntityId, enderman.PositionX, enderman.PositionY, enderman.PositionZ, flags: 0);
            peer.Session.Protocol.World.SendLevelSoundEvent("mob.endermen.portal", enderman.PositionX, enderman.PositionY, enderman.PositionZ);
        }
    }
}
