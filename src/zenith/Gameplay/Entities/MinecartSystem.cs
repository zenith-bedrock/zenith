using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXI — second ECS-authoritative actor, and the one that keeps the ECS from becoming a
/// "mob ECS": no AI, no targeting, and its one feature-specific component
/// (<see cref="VehicleOccupancy"/>) is a player relationship, not a combat/AI state. Position/
/// Health/Velocity/ActorIdentity/DespawnTracking live in <see cref="EntityRuntime"/>, same as
/// Zombie's. The old <c>Minecart</c> object and <c>MinecartStore</c> are gone. See
/// docs/history/phases/phase-xxi-ecs-foundation-findings.md.
/// </summary>
sealed class MinecartSystem : IGameSystem
{
    private const float SpawnDistance = 5f;
    private const float InteractDistance = 2.25f;
    private const float RideForwardImpulse = 0.06f; // Constant creep while occupied — steering comes entirely from the rider's Yaw, not from analog input.
    private const float FrictionPerTick = 0.9f; // Multiplicative decay — rolls further than a mob's knockback, less abrupt stop.
    private const float VelocityNegligible = 0.001f;
    private const string LootItemName = "minecraft:minecart";
    private const float AttackDamage = 4f;
    private const int KillExperience = 1;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<VehicleOccupancy> _minecarts;
    private readonly StackId _lootItem;
    private readonly HashSet<(long EntityId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long EntityId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.
    private bool _bootstrapSpawned;

    public MinecartSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _minecarts = new ComponentStore<VehicleOccupancy>(stores.Entities);
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    internal IReadOnlyList<EntityId> Minecarts => _minecarts.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<VehicleOccupancy> Occupancy => _minecarts;

    /// <summary>Cross-species dispatch seam (Phase XXI, Projectile's generalized hit query) — "is this ECS entity a minecart," nothing more.</summary>
    internal bool Owns(EntityId id) => _minecarts.Has(id);
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }
    internal long MountCount { get; private set; }
    internal long DismountCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_minecarts.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapMinecart(online);

        _tickScratch.Clear();
        _tickScratch.AddRange(_minecarts.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            TryReleaseInvalidOccupant(id, online);
            if (TryDespawn(id, clock, online)) continue;
            ReconcileViewers(id, online);
            ApplyPlayerAttacks(id, online, clock.CurrentTick);
            if (!_stores.Entities.IsAlive(id)) continue;
            TryHandleMountInteraction(id, online);
            var occupant = ResolveOccupant(id, online);
            if (occupant is not null)
                ApplyRiderSteering(id, occupant);
            ApplyRollingMotion(id);
            if (occupant is not null)
                FollowOccupant(id, occupant);
            ReconcileViewers(id, online);
        }

        _replicated.RemoveWhere(pair => !IsKnownAliveMinecartId(pair.EntityId) || !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private bool IsKnownAliveMinecartId(long actorUniqueId)
    {
        foreach (var id in _minecarts.Entities)
            if (_stores.Identities.TryGet(id, out var identity) && identity.ActorUniqueId == actorUniqueId)
                return true;
        return false;
    }

    private void EnsureBootstrapMinecart(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _minecarts.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnDistance;
        var z = player.PositionZ - SpawnDistance;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        SpawnMinecart(x, y, z);
        _bootstrapSpawned = true;
    }

    internal EntityId SpawnMinecart(float x, float y, float z)
    {
        var actorUniqueId = _players.AllocateRuntimeId();
        var id = _stores.CreateActor(actorUniqueId, (ulong)actorUniqueId, x, y, z)
                 ?? throw new InvalidOperationException("Duplicate actor runtime id allocated for a new Minecart.");
        _stores.Health.Set(id, new HealthComponent { State = new HealthState(6f) }); // Vanilla-parity: minecarts are fragile.
        _stores.Velocities.Set(id, new Velocity());
        _stores.Despawn.Set(id, new DespawnTracking());
        _minecarts.Set(id, new VehicleOccupancy());
        return id;
    }

    private Player.Player? ResolveOccupant(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_minecarts.TryGet(id, out var occupancy) || occupancy.OccupantPlayerRuntimeId is not { } riderId) return null;
        return online.FirstOrDefault(p => p.RuntimeId == riderId);
    }

    /// <summary>
    /// Deterministic, exactly-once teardown for every way the relationship can end outside a
    /// voluntary dismount interact: disconnect, death, or the occupant simply no longer resolving.
    /// </summary>
    private void TryReleaseInvalidOccupant(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_minecarts.TryGet(id, out var occupancy) || occupancy.OccupantPlayerRuntimeId is null) return;
        var occupant = ResolveOccupant(id, online);
        if (occupant is not null && occupant.IsInGame && !occupant.IsDead) return;
        Dismount(id, online, occupant);
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. An occupied cart is dismounted first so despawn can never strand a rider.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return false;
        ref var tracking = ref _stores.Despawn.GetRef(id);
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            pos.X, pos.Z, online, DespawnRadius, clock.CurrentTick, tracking.LastSeenNearPlayerTick);
        tracking.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        Dismount(id, online, ResolveOccupant(id, online));
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
        _minecarts.TryGet(id, out var occupancy);

        ViewerReconciliation.Sync(
            identity.ActorUniqueId, online, _replicated,
            peer => ActorInterest.Includes(peer, pos.X, pos.Z),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddMinecart(identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, pos.Yaw);
                peer.Session.Protocol.Entity.SendHealth(identity.ActorRuntimeId, health.Current, health.Maximum);
                // Late join / re-entering interest while occupied: this peer never saw the mount happen, so it must be told now.
                if (occupancy.OccupantPlayerRuntimeId is { } riderId)
                    peer.Session.Protocol.Entity.SendSetActorLink(riderId, identity.ActorUniqueId, SetActorLinkPacket.TypeRider);
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

    private void ApplyPlayerAttacks(EntityId id, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        const float attackDistance = 2.25f;
        DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, attackDistance, AttackDamage, currentTick, TryApplyDamage);
    }

    /// <summary>
    /// Concrete Minecart health/removal operation — same shape as every other migrated actor's,
    /// drops itself as an item. An occupied cart is dismounted so a kill can never strand a rider —
    /// but unlike the pre-ECS version, the occupant/identity must be captured BEFORE calling
    /// <see cref="DamageableActorCombat.TryApplyDamage"/>: on death that call destroys the entity,
    /// which structurally wipes <see cref="VehicleOccupancy"/> along with everything else, so there
    /// is nothing left to read afterward. The dismount's `SetActorLink(Remove)` rides along inside
    /// <c>onDeathReplicatedToPeer</c> — the same peer loop already sending `RemoveActor` — rather
    /// than a second post-destroy broadcast that would have nothing to look up.
    /// </summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        var occupant = ResolveOccupant(id, online);
        if (!_stores.Identities.TryGet(id, out var identityBeforeDamage)) return false;
        var actorUniqueId = identityBeforeDamage.ActorUniqueId;

        var applied = DamageableActorCombat.TryApplyDamage(
            id, _stores, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Minecart", currentTick,
            destroyActor: mid =>
            {
                if (_stores.Identities.TryGet(mid, out var identity))
                    _stores.DestroyActor(identity.ActorRuntimeId, mid);
            },
            onDeathReplicatedToPeer: peer =>
            {
                _lastProjected.Remove((actorUniqueId, peer.RuntimeId));
                if (occupant is not null)
                    peer.Session.Protocol.Entity.SendSetActorLink(occupant.RuntimeId, actorUniqueId, SetActorLinkPacket.TypeRemove);
                ReplicatedRemovalCount++;
            });

        // Gated on actual death (entity no longer alive), not just `applied` — a non-lethal hit
        // also returns true from DamageableActorCombat.TryApplyDamage and must not dismount.
        if (applied && !_stores.Entities.IsAlive(id) && occupant is not null)
        {
            if (occupant.RidingEntityId == actorUniqueId)
                occupant.RidingEntityId = null;
            DismountCount++;
        }
        return applied;
    }

    /// <summary>
    /// The riding relationship's only writer. Toggles: unoccupied → mount the interacting player;
    /// occupied by that same player → dismount; occupied by someone else → no-op.
    /// </summary>
    private void TryHandleMountInteraction(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        var reachSquared = InteractDistance * InteractDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = pos.X - player.PositionX;
            var dz = pos.Z - player.PositionZ;
            if (dx * dx + dz * dz > reachSquared) continue;
            if (!_stores.Identities.TryGet(id, out var identity)) return;
            if (!player.TryConsumeInteractIntent(checked((long)identity.ActorRuntimeId))) continue;

            _minecarts.TryGet(id, out var occupancy);
            if (occupancy.OccupantPlayerRuntimeId is null)
            {
                if (player.RidingEntityId is not null) return; // Already riding something else — not this vehicle's call to resolve.
                Mount(id, player, online);
            }
            else if (occupancy.OccupantPlayerRuntimeId == player.RuntimeId)
            {
                Dismount(id, online, player);
            }
            return;
        }
    }

    private void Mount(EntityId id, Player.Player player, IReadOnlyList<Player.Player> online)
    {
        ref var occupancy = ref _minecarts.GetRef(id);
        occupancy.OccupantPlayerRuntimeId = player.RuntimeId;
        player.RidingEntityId = GetActorUniqueId(id);
        MountCount++;
        BroadcastLink(id, online, player.RuntimeId, SetActorLinkPacket.TypeRider);
    }

    /// <summary>
    /// Idempotent by construction: called from every path that can end the relationship
    /// (voluntary dismount, disconnect, death, despawn). If the cart is already unoccupied this is
    /// a no-op, so exactly-once teardown holds even if two of those paths race in the same tick.
    /// </summary>
    private void Dismount(EntityId id, IReadOnlyList<Player.Player> online, Player.Player? occupant)
    {
        if (!_minecarts.TryGet(id, out var current) || current.OccupantPlayerRuntimeId is not { } riderId) return;
        ref var occupancy = ref _minecarts.GetRef(id);
        occupancy.OccupantPlayerRuntimeId = null;
        var actorUniqueId = GetActorUniqueId(id);
        if (occupant is not null && occupant.RidingEntityId == actorUniqueId)
            occupant.RidingEntityId = null;
        DismountCount++;
        BroadcastLink(id, online, riderId, SetActorLinkPacket.TypeRemove);
    }

    private long GetActorUniqueId(EntityId id) => _stores.Identities.TryGet(id, out var identity) ? identity.ActorUniqueId : 0;

    private void BroadcastLink(EntityId id, IReadOnlyList<Player.Player> online, long riderRuntimeId, byte linkType)
    {
        var actorUniqueId = GetActorUniqueId(id);
        foreach (var peer in online)
            if (_replicated.Contains((actorUniqueId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendSetActorLink(riderRuntimeId, actorUniqueId, linkType);
    }

    /// <summary>Steering: a constant forward creep along the rider's current look direction — no analog input is trusted while mounted (see MovementSystem's Phase XX carve-out).</summary>
    private void ApplyRiderSteering(EntityId id, Player.Player rider)
    {
        ref var vel = ref _stores.Velocities.GetRef(id);
        var yawRad = rider.Yaw * (MathF.PI / 180f);
        vel.X += -MathF.Sin(yawRad) * RideForwardImpulse;
        vel.Z += MathF.Cos(yawRad) * RideForwardImpulse;
    }

    private void ApplyRollingMotion(EntityId id)
    {
        if (!_stores.Velocities.TryGet(id, out var vel)) return;
        if (vel.X == 0f && vel.Z == 0f) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;

        var x = pos.X + vel.X;
        var z = pos.Z + vel.Z;
        if (GroundMobMovement.IsSupportedGroundCell(_world, x, pos.Y, z))
        {
            ref var p = ref _stores.Positions.GetRef(id);
            p.X = x;
            p.Z = z;
            p.Yaw = MathF.Atan2(-vel.X, vel.Z) * (180f / MathF.PI);
        }
        else
        {
            ref var v0 = ref _stores.Velocities.GetRef(id);
            v0.X = 0f;
            v0.Z = 0f;
            return;
        }

        ref var v = ref _stores.Velocities.GetRef(id);
        v.X *= FrictionPerTick;
        v.Z *= FrictionPerTick;
        if (MathF.Abs(v.X) < VelocityNegligible) v.X = 0f;
        if (MathF.Abs(v.Z) < VelocityNegligible) v.Z = 0f;
    }

    /// <summary>
    /// The rider's authoritative position mirrors the vehicle every tick. No explicit teleport is
    /// sent to the rider's own client: <see cref="SetActorLinkPacket"/> is Bedrock's documented
    /// mechanism for the client to visually attach a rider to a vehicle and follow it locally —
    /// fighting that with server-pushed teleports was deliberately not attempted. Untested against
    /// a live client; see phase findings for that caveat.
    /// </summary>
    private void FollowOccupant(EntityId id, Player.Player rider)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        rider.PositionX = pos.X;
        rider.PositionY = pos.Y;
        rider.PositionZ = pos.Z;
    }

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var id in _minecarts.Entities)
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
