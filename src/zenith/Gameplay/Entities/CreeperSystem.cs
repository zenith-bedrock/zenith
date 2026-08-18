using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// First "different death behavior" ground mob (Phase XV). Combat/loot/XP bookkeeping for a
/// player-caused kill is shared via <see cref="DamageableActorCombat"/>, same as every other
/// migrated mob. The fuse → explosion → area damage chain is entirely local to this system: it is
/// not a form of "taking damage," it is a self-triggered removal that happens to reuse
/// <see cref="DamageableActorCombat.TryApplyDamage"/> for its own death/loot/XP/removal bookkeeping
/// (a lethal self-inflicted hit is still exactly that bookkeeping) while area damage to nearby
/// players is applied separately through the ordinary <see cref="PlayerDamage"/> path.
///
/// ECS-authoritative since this migration (see docs/decisions.md, the ADR after §131): Position/
/// Health/ActorIdentity/DespawnTracking live in the shared <see cref="EntityRuntime"/> stores,
/// Velocity.Y is reused as gravity fall-speed (Phase XXIX convention), and only
/// <see cref="CreeperState"/> (target, fuse state) is feature-specific. The fuse/explosion/
/// area-damage logic itself did not need any new ECS primitive: the explosion loop already reads
/// world-space coordinates and calls <see cref="PlayerDamage.ApplyCore"/> per player, independent
/// of what backs the Creeper's own storage.
/// </summary>
sealed class CreeperSystem : IGameSystem
{
    private const float SpawnDistance = 6f;
    private const float DetectionDistance = 16f;
    private const float IgniteDistance = 3f;
    /// <summary>
    /// Phase XXIII-B polish pass — separate ignite/defuse thresholds (hysteresis), matching
    /// PowerNukkitX's <c>EntityCreeper</c> (ignites at ≤3 blocks, only defuses at ≥7 blocks;
    /// real vanilla has this same start/stop asymmetry). Using one shared threshold for both made a
    /// creeper standing at exactly ~3 blocks flicker its Ignited flag on/off every tick.
    /// </summary>
    private const float DefuseDistance = 7f;
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
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<CreeperState> _creepers;
    private readonly StackId _lootItem;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private readonly List<EntityId> _tickScratch = [];
    private bool _bootstrapSpawned;

    public CreeperSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _creepers = new ComponentStore<CreeperState>(stores.Entities);
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    internal IReadOnlyList<EntityId> Creepers => _creepers.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<CreeperState> CreeperStates => _creepers;

    /// <summary>Cross-species dispatch seam (Phase XXII) — "is this ECS entity a creeper," nothing more.</summary>
    internal bool Owns(EntityId id) => _creepers.Has(id);
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long ExplosionCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_creepers.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapCreeper(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_creepers.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;

            var target = FindOrAcquireTarget(id, online);
            ref var state = ref _creepers.GetRef(id);
            var exitThreshold = state.IsFusing ? DefuseDistance : IgniteDistance;
            if (target is null)
            {
                Defuse(id, online);
            }
            else if (DistanceSquared(id, target) > exitThreshold * exitThreshold)
            {
                Defuse(id, online);
                AdvanceTowardTarget(id, target);
            }
            else
            {
                TickFuse(id, clock, online);
            }

            if (_stores.Entities.IsAlive(id))
            {
                ApplyGravity(id);
                ReconcileViewers(id, online);
            }
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveCreeperId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private bool IsKnownAliveCreeperId(long actorUniqueId)
    {
        foreach (var id in _creepers.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    private void EnsureBootstrapCreeper(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _creepers.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnDistance;
        var z = player.PositionZ;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        SpawnCreeper(x, y, z);
        _bootstrapSpawned = true;
    }

    /// <summary>The feature-specific composition step every migrated actor needs on top of <see cref="EntityRuntime.CreateActor"/>.</summary>
    internal EntityId SpawnCreeper(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Creeper.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(20f) }); // Vanilla-parity creeper health.
        _stores.Velocities.Set(id, new Velocity()); // Phase XXIX: Y reused as gravity fall-speed.
        _stores.Despawn.Set(id, new DespawnTracking());
        _creepers.Set(id, new CreeperState());
        return id;
    }

    /// <summary>
    /// No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. A fusing
    /// creeper can never actually reach this: a player must be within ignite range (3) to fuse,
    /// which is always inside despawn range (64), so <see cref="DespawnLifecycle.EvaluateDespawn"/>
    /// naturally returns false for it without any special-casing here.
    /// </summary>
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
                peer.Session.Protocol.Entity.SendAddCreeper(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
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
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, PlayerAttackDamage, currentTick, TryApplyDamage);

    /// <summary>Player kills it with a weapon before the fuse completes — ordinary shared bookkeeping.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Creeper", currentTick,
            destroyActor: cid =>
            {
                if (_stores.Identities.TryGet(cid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, cid);
            },
            onDeathReplicatedToPeer: peer =>
            {
                if (_stores.Identities.TryGet(id, out var identity))
                    _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
                ReplicatedRemovalCount++;
            });

    /// <summary>
    /// Phase XXIII fix — Creeper never broadcast its Ignited state at all, so a real client never
    /// showed the fuse-lit swell/flash animation. FLAGS bit 10 confirmed against bedrock-protocol's
    /// <c>EntityMetadataFlags::IGNITED</c> and gophertunnel's <c>EntityDataFlagIgnited</c> (both match
    /// Zenith's existing Sneaking/Sprinting/ShowName bit numbers exactly, high confidence). Sent only
    /// on the false→true/true→false transition, not every tick.
    /// </summary>
    private void SetIgnited(EntityId id, bool ignited, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _creepers.GetRef(id);
        if (state.IsFusing == ignited) return;
        state.IsFusing = ignited;

        if (!_stores.Identities.TryGet(id, out var identity)) return;
        foreach (var peer in online)
            if (_replicated.Contains((identity.ActorUniqueId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendActorIgnited(identity.ActorRuntimeId, ignited);
    }

    private void Defuse(EntityId id, IReadOnlyList<Player.Player> online) => SetIgnited(id, false, online);

    private void TickFuse(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _creepers.GetRef(id);
        if (!state.IsFusing)
        {
            SetIgnited(id, true, online);
            ref var restarted = ref _creepers.GetRef(id);
            restarted.FuseStartedTick = clock.CurrentTick;
            return;
        }

        if (clock.CurrentTick - state.FuseStartedTick >= FuseDurationTicks)
            Explode(id, online, clock.CurrentTick);
    }

    /// <summary>
    /// Self-kill reuses DamageableActorCombat's death/loot/XP/removal bookkeeping (an explosion is
    /// still exactly that transition, just self-inflicted with no attacker to attribute XP to).
    /// Area damage to nearby players is a distinct, Creeper-only concept — the combat helper never
    /// touches player health.
    /// </summary>
    private void Explode(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var explosionX = pos.X;
        var explosionY = pos.Y;
        var explosionZ = pos.Z;
        if (!_stores.Health.TryGet(id, out var healthComponent)) return;
        var maxHealth = healthComponent.State.Maximum;

        if (!DamageableActorCombat.TryApplyDamage(
                id, _stores, DamageSource.Generic, maxHealth, online, _world, _players, _replicated,
                _lootItem, KillExperience, "Creeper", currentTick,
                destroyActor: cid =>
                {
                    if (_stores.Identities.TryGet(cid, out var identity))
                        _stores.DestroyActor(identity.ActorRuntimeId, cid);
                },
                onDeathReplicatedToPeer: peer =>
                {
                    if (_stores.Identities.TryGet(id, out var identity))
                        _lastProjected.Remove((identity.ActorUniqueId, peer.RuntimeId));
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
            PlayerDamage.ApplyCore(player, _players, DamageSource.Generic, damage, currentTick).Conclude(online);
        }

        var blockUpdates = BreakBlocksInRadius(explosionX, explosionY, explosionZ);

        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.World.SendLevelEvent(LevelEventPacket.EventParticleExplosion, explosionX, explosionY, explosionZ);
            peer.Session.Protocol.World.SendLevelSoundEvent("explode", explosionX, explosionY, explosionZ);
            if (blockUpdates.Count > 0)
                peer.Session.Protocol.World.PublishUpdateBlocks(blockUpdates);
        }
    }

    /// <summary>
    /// Phase XXIII fix — Creeper explosions never touched the world at all (no crater), even though
    /// they already applied player area damage. Simple spherical carve (distance falloff, not a full
    /// blast-resistance/ray-casting simulation) — every solid, non-bedrock block within
    /// <see cref="ExplosionRadius"/> becomes air. No item drops for destroyed terrain, matching
    /// vanilla's default (mobGriefing-independent) "no block drops from mob-caused explosions".
    /// </summary>
    private List<(int X, int Y, int Z, int BlockRuntimeId)> BreakBlocksInRadius(float centerX, float centerY, float centerZ)
    {
        var updates = new List<(int, int, int, int)>();
        var radius = (int)MathF.Ceiling(ExplosionRadius);
        var cx = (int)MathF.Floor(centerX);
        var cy = (int)MathF.Floor(centerY);
        var cz = (int)MathF.Floor(centerZ);

        for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (distance > ExplosionRadius) continue;

                    var x = cx + dx;
                    var y = cy + dy;
                    var z = cz + dz;
                    var current = _world.GetBlock(x, y, z);
                    if (current == World.World.AirRuntimeId || current == Blocks.Bedrock) continue;
                    if (!_world.TrySetBlock(x, y, z, World.World.AirRuntimeId)) continue;
                    updates.Add((x, y, z, World.World.AirRuntimeId));
                }

        return updates;
    }

    private Player.Player? FindOrAcquireTarget(EntityId id, IReadOnlyList<Player.Player> online)
    {
        ref var state = ref _creepers.GetRef(id);
        if (state.TargetPlayerRuntimeId is { } retainedId)
        {
            var retained = _players.GetByRuntimeId(retainedId);
            if (retained is not null && IsTargetValid(id, retained))
                return retained;
            state.TargetPlayerRuntimeId = null;
        }

        if (!_stores.Positions.TryGet(id, out var pos)) return null;

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

        ref var updated = ref _creepers.GetRef(id);
        updated.TargetPlayerRuntimeId = target?.RuntimeId;
        return target;
    }

    private bool IsTargetValid(EntityId id, Player.Player target)
    {
        if (!target.IsInGame || target.IsDead) return false;
        return DistanceSquared(id, target) <= DetectionDistance * DetectionDistance;
    }

    private float DistanceSquared(EntityId id, Player.Player target)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return float.MaxValue;
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        return dx * dx + dz * dz;
    }

    /// <summary>Direct approach only (no side-step fallback) — a creeper stalling at an obstacle just re-tries next tick.</summary>
    private void AdvanceTowardTarget(EntityId id, Player.Player target)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        var lengthSquared = dx * dx + dz * dz;
        if (lengthSquared <= 0.0001f) return;

        var length = MathF.Sqrt(lengthSquared);
        var stepX = pos.X + dx / length * MovePerTick;
        var stepZ = pos.Z + dz / length * MovePerTick;
        if (!GroundMobMovement.TryMoveHorizontal(_world, pos.X, pos.Y, pos.Z, stepX, stepZ, out var resolvedY)) return;

        ref var p = ref _stores.Positions.GetRef(id);
        p.X = stepX;
        p.Y = resolvedY;
        p.Z = stepZ;
        p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(dx, dz), LookMath.DefaultMaxTurnDegreesPerTick);
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
            foreach (var id in _creepers.Entities)
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
}
