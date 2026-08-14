using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Phase XVIII, Priority 4 — a boss-shaped pressure test on "does behavior complexity exceed what
/// one concrete System can reasonably own." It does not: phase transition, melee, and the area slam
/// are three straightforward concrete blocks in this one file, same shape as every other mob
/// system. No component system was needed. See docs/history/phases/phase-xviii-runtime-pressure-findings.md.
///
/// Melee/loot/XP bookkeeping reuses <see cref="GroundMobCombat"/> unmodified. The area slam
/// (<see cref="TrySlam"/>) is the first mob ability to damage more than one player in a single
/// action — it turned out to need nothing new: <see cref="PlayerDamage.Apply"/> is already
/// per-player, so looping over everyone in range was sufficient. Player-side knockback was
/// deliberately NOT added: player position is client-authoritative everywhere else in this
/// codebase (see docs/entities.md's capability matrix), and inventing server-pushed player
/// movement for one ability would be exactly the kind of speculative mechanism this project avoids
/// building ahead of a second real need.
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
    private const string LootItemName = "minecraft:iron_ingot";
    /// <summary>Boss-tier kill reward — five times a hostile ground mob's (Phase XI.4/XIV precedent).</summary>
    private const int KillExperience = 25;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly GolemStore _golems;
    private readonly StackId _lootItem;
    private readonly HashSet<(long GolemId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long GolemId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private bool _bootstrapSpawned;
    private ulong _currentTick;

    public GolemSystem(World.World world, PlayerManager players, GolemStore golems, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _golems = golems;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    public GolemStore Golems => _golems;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }
    internal long SlamCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _currentTick = clock.CurrentTick;
        if (online.Count == 0) return;
        if (_golems.Active.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapGolem(online);

        foreach (var golem in _golems.Active.ToArray())
        {
            if (!golem.IsActive) continue;
            if (TryDespawn(golem, clock, online)) continue;
            ReconcileViewers(golem, online);
            ApplyPlayerAttacks(golem, online);
            if (!golem.IsActive) continue;

            golem.IsEnraged = golem.Health.Current <= golem.Health.Maximum * EnrageHealthFraction;

            var target = FindProvokedTarget(golem, online);
            if (target is not null)
            {
                AdvanceTowardTarget(golem, target);
                TryAttackPlayer(golem, target, clock, online);
            }
            if (golem.IsEnraged)
                TrySlam(golem, online, clock);

            ReconcileViewers(golem, online);
        }

        _replicated.RemoveWhere(pair => !_golems.Active.Any(g => g.EntityId == pair.GolemId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private void EnsureBootstrapGolem(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _golems.Active.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnDistance;
        var z = player.PositionZ + SpawnDistance;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        var entityId = _players.AllocateRuntimeId();
        if (_golems.TryAdd(new Golem(entityId, (ulong)entityId, x, y, z)))
            _bootstrapSpawned = true;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(Golem golem, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            golem.PositionX, golem.PositionZ, online, DespawnRadius, clock.CurrentTick, golem.LastSeenNearPlayerTick);
        golem.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        foreach (var peer in online)
            if (_replicated.Remove((golem.EntityId, peer.RuntimeId)))
            {
                _lastProjected.Remove((golem.EntityId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(golem.EntityId);
                ReplicatedRemovalCount++;
            }
        golem.Remove();
        _golems.Remove(golem);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(Golem golem, IReadOnlyList<Player.Player> online) =>
        ViewerReconciliation.Sync(
            golem.EntityId, online, _replicated,
            peer => ActorInterest.Includes(peer, golem.PositionX, golem.PositionZ),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddGolem(
                    golem.EntityId, golem.RuntimeId, golem.PositionX, golem.PositionY, golem.PositionZ, golem.Yaw);
                peer.Session.Protocol.Entity.SendHealth(golem.RuntimeId, golem.Health.Current, golem.Health.Maximum);
                _lastProjected[(golem.EntityId, peer.RuntimeId)] = new ProjectedPose(golem.PositionX, golem.PositionY, golem.PositionZ, golem.Yaw);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                _lastProjected.Remove((golem.EntityId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(golem.EntityId);
                ReplicatedRemovalCount++;
            });

    private void ApplyPlayerAttacks(Golem golem, IReadOnlyList<Player.Player> online) =>
        GroundMobCombat.ApplyPlayerMeleeAttacks(golem, online, AttackDistance, AttackDamage, _currentTick, TryApplyDamage);

    /// <summary>Concrete Golem health/removal operation — same shape as every other ground mob's. A player attacker provokes retaliation (self-defense — vanilla golems always fight back).</summary>
    public bool TryApplyDamage(Golem golem, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        if (source.OwnerRuntimeId is { } attackerId)
            golem.TargetPlayerRuntimeId = attackerId;

        return GroundMobCombat.TryApplyDamage(
            golem, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Golem", currentTick,
            removeFromStore: _golems.Remove,
            onDeathReplicatedToPeer: peer =>
            {
                _lastProjected.Remove((golem.EntityId, peer.RuntimeId));
                ReplicatedRemovalCount++;
            });
    }

    /// <summary>
    /// Phase XXIII-B — replaces the old "always attack the nearest player" boss behavior: a golem
    /// stays passive until provoked (see <see cref="ProvokeWindowTicks"/>'s doc comment), then
    /// retains that specific player as its target the same way Zombie/Spider retain theirs.
    /// </summary>
    private Player.Player? FindProvokedTarget(Golem golem, IReadOnlyList<Player.Player> online)
    {
        if (golem.TargetPlayerRuntimeId is { } retainedId)
        {
            var retained = online.FirstOrDefault(p => p.RuntimeId == retainedId);
            if (retained is not null && IsTargetValid(golem, retained))
                return retained;
            golem.TargetPlayerRuntimeId = null;
        }

        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            if (player.LastVillagerAttack is not { } attack) continue;
            if (_currentTick - attack.Tick > ProvokeWindowTicks) continue;

            var dx = attack.X - golem.PositionX;
            var dz = attack.Z - golem.PositionZ;
            if (dx * dx + dz * dz > DetectionDistance * DetectionDistance) continue;

            golem.TargetPlayerRuntimeId = player.RuntimeId;
            return player;
        }

        return null;
    }

    private static bool IsTargetValid(Golem golem, Player.Player target)
    {
        if (!target.IsInGame || target.IsDead) return false;
        var dx = target.PositionX - golem.PositionX;
        var dz = target.PositionZ - golem.PositionZ;
        return dx * dx + dz * dz <= DetectionDistance * DetectionDistance;
    }

    private void AdvanceTowardTarget(Golem golem, Player.Player target)
    {
        var dx = target.PositionX - golem.PositionX;
        var dz = target.PositionZ - golem.PositionZ;
        var distanceSquared = dx * dx + dz * dz;
        if (distanceSquared <= AttackDistance * AttackDistance || distanceSquared <= 0.0001f) return;

        var length = MathF.Sqrt(distanceSquared);
        var dxn = dx / length;
        var dzn = dz / length;
        var distance = MathF.Min(MovePerTick, length - AttackDistance);
        if (TryMove(golem, golem.PositionX + dxn * distance, golem.PositionZ + dzn * distance))
            golem.Yaw = LookMath.MoveYawTowards(golem.Yaw, LookMath.YawTowards(dxn, dzn), LookMath.DefaultMaxTurnDegreesPerTick);
    }

    /// <summary>Concrete Golem rule: where to step. Validity itself is shared (<see cref="GroundMobMovement"/>).</summary>
    private bool TryMove(Golem golem, float x, float z)
    {
        if (!GroundMobMovement.CanStandAt(_world, x, golem.PositionY, z)) return false;
        golem.PositionX = x;
        golem.PositionZ = z;
        return true;
    }

    private void TryAttackPlayer(Golem golem, Player.Player target, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick < golem.NextAttackTick) return;
        var dx = target.PositionX - golem.PositionX;
        var dz = target.PositionZ - golem.PositionZ;
        if (dx * dx + dz * dz > AttackDistance * AttackDistance) return;
        if (PlayerDamage.Apply(target, _players, online, DamageSource.MeleeFrom(golem.EntityId), AttackDamage, clock.CurrentTick, dx, dz))
        {
            golem.NextAttackTick = clock.CurrentTick + AttackCooldownTicks;
            foreach (var peer in online)
                if (_replicated.Contains((golem.EntityId, peer.RuntimeId)))
                    peer.Session.Protocol.Entity.SendAttackSwing(golem.RuntimeId);
        }
    }

    /// <summary>
    /// Area ability, only reachable once enraged: damages every player within <see cref="SlamRadius"/>
    /// in one action. The first mob ability to hit more than one player at once — turned out to need
    /// no new primitive, just a loop over <see cref="PlayerDamage.Apply"/>, which was already
    /// per-player and had no assumption baked in that only one player could be hit per call.
    /// </summary>
    private void TrySlam(Golem golem, IReadOnlyList<Player.Player> online, GameClock clock)
    {
        if (clock.CurrentTick < golem.NextSlamTick) return;
        var radiusSquared = SlamRadius * SlamRadius;
        var hitAny = false;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = player.PositionX - golem.PositionX;
            var dz = player.PositionZ - golem.PositionZ;
            if (dx * dx + dz * dz > radiusSquared) continue;
            if (PlayerDamage.Apply(player, _players, online, DamageSource.MeleeFrom(golem.EntityId), SlamDamage, clock.CurrentTick, dx, dz))
                hitAny = true;
        }

        if (!hitAny) return;
        golem.NextSlamTick = clock.CurrentTick + SlamCooldownTicks;
        SlamCount++;
        foreach (var peer in online)
            if (peer.IsInGame)
                peer.Session.Protocol.World.SendLevelSoundEvent("mob.irongolem.attack", golem.PositionX, golem.PositionY, golem.PositionZ);
    }

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var golem in _golems.Active)
            {
                var key = (golem.EntityId, peer.RuntimeId);
                if (!_replicated.Contains(key)) continue;
                var current = new ProjectedPose(golem.PositionX, golem.PositionY, golem.PositionZ, golem.Yaw);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                {
                    ReplicatedMoveSkippedCount++;
                    continue;
                }
                _moveBatch.Add(new RawActorPose
                {
                    ActorRuntimeId = golem.RuntimeId,
                    X = golem.PositionX,
                    Y = golem.PositionY,
                    Z = golem.PositionZ,
                    Yaw = golem.Yaw,
                    HeadYaw = golem.Yaw
                });
                _lastProjected[key] = current;
                ReplicatedMoveCount++;
            }
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }
}
