using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// First "different death behavior" ground mob (Phase XV). Combat/loot/XP bookkeeping for a
/// player-caused kill is shared via <see cref="GroundMobCombat"/>, same as every other ground mob.
/// The fuse → explosion → area damage chain is entirely local to this system: it is not a form of
/// "taking damage," it is a self-triggered removal that happens to reuse
/// <see cref="GroundMobCombat.TryApplyDamage"/> for its own death/loot/XP/removal bookkeeping (a
/// lethal self-inflicted hit is still exactly that bookkeeping) while area damage to nearby players
/// is applied separately through the ordinary <see cref="PlayerDamage"/> path.
/// </summary>
sealed class CreeperSystem : IGameSystem
{
    private const float SpawnDistance = 6f;
    private const float DetectionDistance = 16f;
    private const float IgniteDistance = 3f;
    private const float AttackDistance = 2.25f;
    /// <summary>Damage a player's weapon deals per hit — unrelated to <see cref="ExplosionDamage"/>.</summary>
    private const float PlayerAttackDamage = 4f;
    private const float MovePerTick = 0.08f;
    private const int FuseDurationTicks = 30; // 1.5s @ 20 TPS, vanilla-parity.
    private const float ExplosionRadius = 3f;
    private const float ExplosionDamage = 10f;
    private const string LootItemName = "minecraft:gunpowder";
    /// <summary>Vanilla-parity hostile-mob kill reward (Phase XI.4) — only paid for a player-caused kill.</summary>
    private const int KillExperience = 5;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly CreeperStore _creepers;
    private readonly StackId _lootItem;
    private readonly HashSet<(long CreeperId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long CreeperId, long PlayerId), ProjectedPosition> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private bool _bootstrapSpawned;

    public CreeperSystem(World.World world, PlayerManager players, CreeperStore creepers, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _creepers = creepers;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    public CreeperStore Creepers => _creepers;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long ExplosionCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_creepers.Active.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapCreeper(online);

        foreach (var creeper in _creepers.Active.ToArray())
        {
            if (!creeper.IsActive) continue;
            if (TryDespawn(creeper, clock, online)) continue;
            ReconcileViewers(creeper, online);
            ApplyPlayerAttacks(creeper, online);
            if (!creeper.IsActive) continue;

            var target = FindOrAcquireTarget(creeper, online);
            if (target is null)
            {
                Defuse(creeper);
            }
            else if (DistanceSquared(creeper, target) > IgniteDistance * IgniteDistance)
            {
                Defuse(creeper);
                AdvanceTowardTarget(creeper, target);
            }
            else
            {
                TickFuse(creeper, clock, online);
            }

            if (creeper.IsActive)
                ReconcileViewers(creeper, online);
        }

        _replicated.RemoveWhere(pair => !_creepers.Active.Any(c => c.EntityId == pair.CreeperId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private void EnsureBootstrapCreeper(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _creepers.Active.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnDistance;
        var z = player.PositionZ;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        var entityId = _players.AllocateRuntimeId();
        if (_creepers.TryAdd(new Creeper(entityId, (ulong)entityId, x, y, z)))
            _bootstrapSpawned = true;
    }

    /// <summary>
    /// No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. A fusing
    /// creeper can never actually reach this: a player must be within ignite range (3) to fuse,
    /// which is always inside despawn range (64), so <see cref="DespawnLifecycle.EvaluateDespawn"/>
    /// naturally returns false for it without any special-casing here.
    /// </summary>
    private bool TryDespawn(Creeper creeper, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            creeper.PositionX, creeper.PositionZ, online, DespawnRadius, clock.CurrentTick, creeper.LastSeenNearPlayerTick);
        creeper.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        foreach (var peer in online)
            if (_replicated.Remove((creeper.EntityId, peer.RuntimeId)))
            {
                _lastProjected.Remove((creeper.EntityId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(creeper.EntityId);
                ReplicatedRemovalCount++;
            }
        creeper.Remove();
        _creepers.Remove(creeper);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(Creeper creeper, IReadOnlyList<Player.Player> online) =>
        ViewerReconciliation.Sync(
            creeper.EntityId, online, _replicated,
            peer => ActorInterest.Includes(peer, creeper.PositionX, creeper.PositionZ),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddCreeper(
                    creeper.EntityId, creeper.RuntimeId, creeper.PositionX, creeper.PositionY, creeper.PositionZ, creeper.Yaw);
                peer.Session.Protocol.Entity.SendHealth(creeper.RuntimeId, creeper.Health.Current, creeper.Health.Maximum);
                _lastProjected[(creeper.EntityId, peer.RuntimeId)] = new ProjectedPosition(creeper.PositionX, creeper.PositionY, creeper.PositionZ);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                _lastProjected.Remove((creeper.EntityId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(creeper.EntityId);
                ReplicatedRemovalCount++;
            });

    private void ApplyPlayerAttacks(Creeper creeper, IReadOnlyList<Player.Player> online) =>
        GroundMobCombat.ApplyPlayerMeleeAttacks(creeper, online, AttackDistance, PlayerAttackDamage, TryApplyDamage);

    /// <summary>Player kills it with a weapon before the fuse completes — ordinary shared bookkeeping.</summary>
    public bool TryApplyDamage(Creeper creeper, DamageSource source, float amount, IReadOnlyList<Player.Player> online) =>
        GroundMobCombat.TryApplyDamage(
            creeper, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Creeper",
            removeFromStore: _creepers.Remove,
            onDeathReplicatedToPeer: peer =>
            {
                _lastProjected.Remove((creeper.EntityId, peer.RuntimeId));
                ReplicatedRemovalCount++;
            });

    private static void Defuse(Creeper creeper) => creeper.IsFusing = false;

    private void TickFuse(Creeper creeper, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (!creeper.IsFusing)
        {
            creeper.IsFusing = true;
            creeper.FuseStartedTick = clock.CurrentTick;
            return;
        }

        if (clock.CurrentTick - creeper.FuseStartedTick >= FuseDurationTicks)
            Explode(creeper, online);
    }

    /// <summary>
    /// Self-kill reuses GroundMobCombat's death/loot/XP/removal bookkeeping (an explosion is still
    /// exactly that transition, just self-inflicted with no attacker to attribute XP to). Area
    /// damage to nearby players is a distinct, Creeper-only concept — GroundMobCombat never touches
    /// player health.
    /// </summary>
    private void Explode(Creeper creeper, IReadOnlyList<Player.Player> online)
    {
        var explosionX = creeper.PositionX;
        var explosionY = creeper.PositionY;
        var explosionZ = creeper.PositionZ;

        if (!GroundMobCombat.TryApplyDamage(
                creeper, DamageSource.Generic, creeper.Health.Maximum, online, _world, _players, _replicated,
                _lootItem, KillExperience, "Creeper", removeFromStore: _creepers.Remove,
                onDeathReplicatedToPeer: peer =>
                {
                    _lastProjected.Remove((creeper.EntityId, peer.RuntimeId));
                    ReplicatedRemovalCount++;
                }))
        {
            return; // Floor-drop capacity is full this tick — retry next tick, no area damage yet.
        }

        ExplosionCount++;

        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = player.PositionX - explosionX;
            var dy = player.PositionY - explosionY;
            var dz = player.PositionZ - explosionZ;
            var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance > ExplosionRadius) continue;
            var damage = ExplosionDamage * (1f - distance / ExplosionRadius);
            if (damage <= 0f) continue;
            _ = PlayerDamage.Apply(player, _players, online, DamageSource.Generic, damage);
        }

        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.World.SendLevelEvent(LevelEventPacket.EventParticleExplosion, explosionX, explosionY, explosionZ);
            peer.Session.Protocol.World.SendLevelSoundEvent("explode", explosionX, explosionY, explosionZ);
        }
    }

    private static Player.Player? FindOrAcquireTarget(Creeper creeper, IReadOnlyList<Player.Player> online)
    {
        if (creeper.TargetPlayerRuntimeId is { } retainedId)
        {
            var retained = online.FirstOrDefault(p => p.RuntimeId == retainedId);
            if (retained is not null && IsTargetValid(creeper, retained))
                return retained;
            creeper.TargetPlayerRuntimeId = null;
        }

        Player.Player? target = null;
        var best = DetectionDistance * DetectionDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var distance = DistanceSquared(creeper, player);
            if (distance >= best) continue;
            best = distance;
            target = player;
        }

        creeper.TargetPlayerRuntimeId = target?.RuntimeId;
        return target;
    }

    private static bool IsTargetValid(Creeper creeper, Player.Player target) =>
        target.IsInGame && !target.IsDead && DistanceSquared(creeper, target) <= DetectionDistance * DetectionDistance;

    private static float DistanceSquared(Creeper creeper, Player.Player target)
    {
        var dx = target.PositionX - creeper.PositionX;
        var dz = target.PositionZ - creeper.PositionZ;
        return dx * dx + dz * dz;
    }

    /// <summary>Direct approach only (no side-step fallback) — a creeper stalling at an obstacle just re-tries next tick.</summary>
    private void AdvanceTowardTarget(Creeper creeper, Player.Player target)
    {
        var dx = target.PositionX - creeper.PositionX;
        var dz = target.PositionZ - creeper.PositionZ;
        var lengthSquared = dx * dx + dz * dz;
        if (lengthSquared <= 0.0001f) return;

        var length = MathF.Sqrt(lengthSquared);
        var stepX = creeper.PositionX + dx / length * MovePerTick;
        var stepZ = creeper.PositionZ + dz / length * MovePerTick;
        if (!GroundMobMovement.CanStandAt(_world, stepX, creeper.PositionY, stepZ)) return;

        creeper.PositionX = stepX;
        creeper.PositionZ = stepZ;
        creeper.Yaw = MathF.Atan2(-dx, dz) * (180f / MathF.PI);
    }

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var creeper in _creepers.Active)
            {
                var key = (creeper.EntityId, peer.RuntimeId);
                if (!_replicated.Contains(key)) continue;
                var current = new ProjectedPosition(creeper.PositionX, creeper.PositionY, creeper.PositionZ);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                    continue;
                _moveBatch.Add(new RawActorPose
                {
                    ActorRuntimeId = creeper.RuntimeId,
                    X = creeper.PositionX,
                    Y = creeper.PositionY,
                    Z = creeper.PositionZ
                });
                _lastProjected[key] = current;
            }
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }

    private readonly record struct ProjectedPosition(float X, float Y, float Z)
    {
        private const float PositionEpsilonSquared = 0.0001f;

        public bool MeaningfullyChanged(ProjectedPosition previous)
        {
            var dx = X - previous.X;
            var dy = Y - previous.Y;
            var dz = Z - previous.Z;
            return dx * dx + dy * dy + dz * dz > PositionEpsilonSquared;
        }
    }
}
