using Zenith.Gameplay.Ports;
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
    private const float AttackDamage = 4f;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<VehicleOccupancy> _minecarts;
    private readonly MobCore _mob;
    private readonly ActorReplication _replication;
    private readonly List<EntityId> _tickScratch = []; // Reused per tick — see ZombieSystem's identical field for why.

    public MinecartSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Minecart, world, players, stores, itemPalette);
        _replication = _mob.Replication;
        _minecarts = new ComponentStore<VehicleOccupancy>(stores.Entities);
    }

    internal IReadOnlyList<EntityId> Minecarts => _minecarts.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<VehicleOccupancy> Occupancy => _minecarts;

    /// <summary>Cross-species dispatch seam (Phase XXI, Projectile's generalized hit query) — "is this ECS entity a minecart," nothing more.</summary>
    internal bool Owns(EntityId id) => _minecarts.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedMoveSkippedCount => _replication.MoveSkippedCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;
    internal long MountCount { get; private set; }
    internal long DismountCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _replication.BeginTick(online);
        if (online.Count == 0) return;

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
            var occupant = ResolveOccupant(id);
            if (occupant is not null)
                ApplyRiderSteering(id, occupant);
            ApplyRollingMotion(id);
            if (occupant is not null)
                FollowOccupant(id, occupant);
            ReconcileViewers(id, online);
        }

        _replication.Prune(_minecarts.Entities, online);
        _replication.ReplicateMoves(_minecarts.Entities, online);
    }


    internal EntityId SpawnMinecart(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        _minecarts.Set(id, new VehicleOccupancy());
        return id;
    }

    private Player.Player? ResolveOccupant(EntityId id)
    {
        if (!_minecarts.TryGet(id, out var occupancy) || occupancy.OccupantPlayerRuntimeId is not { } riderId) return null;
        return _players.GetByRuntimeId(riderId);
    }

    /// <summary>
    /// Deterministic, exactly-once teardown for every way the relationship can end outside a
    /// voluntary dismount interact: disconnect, death, or the occupant simply no longer resolving.
    /// </summary>
    private void TryReleaseInvalidOccupant(EntityId id, IReadOnlyList<Player.Player> online)
    {
        if (!_minecarts.TryGet(id, out var occupancy) || occupancy.OccupantPlayerRuntimeId is null) return;
        var occupant = ResolveOccupant(id);
        if (occupant is not null && occupant.IsInGame && !occupant.IsDead) return;
        Dismount(id, online, occupant);
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. An occupied cart is dismounted first so despawn can never strand a rider.</summary>
    private bool TryDespawn(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online) =>
        _replication.TryDespawn(id, clock, online, DespawnRadius, beforeDestroy: id => Dismount(id, online, ResolveOccupant(id)));

    private void ReconcileViewers(EntityId id, IReadOnlyList<Player.Player> online) =>
        _replication.Reconcile(id, online,
            (peer, identity) =>
            {
                // Late join / re-entering interest while occupied: this peer never saw the mount happen, so it must be told now.
                if (_minecarts.TryGet(id, out var occupancy) && occupancy.OccupantPlayerRuntimeId is { } riderId)
                    peer.Client.Entity.SendSetActorLink(riderId, identity.ActorUniqueId, SetActorLinkPacket.TypeRider);
            });

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
        var occupant = ResolveOccupant(id);
        if (!_stores.Identities.TryGet(id, out var identityBeforeDamage)) return false;
        var actorUniqueId = identityBeforeDamage.ActorUniqueId;

        var applied = _mob.TryApplyDamage(id, source, amount, online, currentTick, onPeerRemoved: peer =>
            {
                if (occupant is not null)
                    peer.Client.Entity.SendSetActorLink(occupant.RuntimeId, actorUniqueId, SetActorLinkPacket.TypeRemove);
            });

        // Gated on actual death (entity no longer alive), not just `applied` — a non-lethal hit
        // also returns true from DamageableActorCombat.TryApplyDamage and must not dismount.
        if (applied && !_stores.Entities.IsAlive(id) && occupant is not null)
        {
            if (occupant.RidingEntityId == actorUniqueId)
                occupant.RidingEntityId = null;
            DismountCount++;
            SendDismountTeleport(occupant);
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
            if (!player.Intents.TryConsumeInteractIntent(checked((long)identity.ActorRuntimeId))) continue;

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
        SendDismountTeleport(occupant);
    }

    /// <summary>
    /// ADR §136 — resolves Phase XX's flagged-but-untested assumption. <see cref="FollowOccupant"/>
    /// repositions the rider's authoritative pose every tick while mounted, but never sent the
    /// rider's own client a packet that says so — <see cref="SetActorLinkPacket"/> only tells peers
    /// to visually attach/detach the rider, it does not resync the rider's own local
    /// position/camera prediction. Without this, the client's own understanding of where it is
    /// silently drifted from the server the entire ride, and nothing ever corrected it — every
    /// server-side reach/interact check afterward measured against wherever the client's stale
    /// prediction landed, not where it visually was, so the player could end up unable to hit or
    /// interact with anything after dismounting (a live-client bug report, not a hypothetical).
    /// Matches the same pattern <see cref="MovementSystem"/>'s respawn path already uses for the
    /// same class of problem (an authoritative reposition outside normal AuthInput flow).
    /// </summary>
    private static void SendDismountTeleport(Player.Player? occupant)
    {
        if (occupant is null || !occupant.IsInGame || occupant.IsDead) return;
        occupant.Client.Entity.SendMovePlayerTeleport(
            entityRuntimeId: (ulong)occupant.RuntimeId,
            x: occupant.PositionX,
            y: occupant.PositionY,
            z: occupant.PositionZ,
            pitch: occupant.Pitch,
            yaw: occupant.Yaw,
            headYaw: occupant.HeadYaw);
    }

    private long GetActorUniqueId(EntityId id) => _stores.Identities.TryGet(id, out var identity) ? identity.ActorUniqueId : 0;

    private void BroadcastLink(EntityId id, IReadOnlyList<Player.Player> online, long riderRuntimeId, byte linkType)
    {
        var actorUniqueId = GetActorUniqueId(id);
        foreach (var peer in online)
            if (_replication.Viewers.Contains((actorUniqueId, peer.RuntimeId)))
                peer.Client.Entity.SendSetActorLink(riderRuntimeId, actorUniqueId, linkType);
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
    /// sent to the rider's own client while still mounted: <see cref="SetActorLinkPacket"/> is
    /// Bedrock's documented mechanism for the client to visually attach a rider to a vehicle and
    /// follow it locally — fighting that with server-pushed teleports every tick was deliberately
    /// not attempted. That part held up. What did not: <see cref="SendDismountTeleport"/> (ADR
    /// §136) — the moment authoritative control hands back to the client, it does need an explicit
    /// correction, since <c>SetActorLinkPacket</c> only turns the visual attachment on/off, it does
    /// not resync the rider's own local position/camera prediction.
    /// </summary>
    private void FollowOccupant(EntityId id, Player.Player rider)
    {
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        rider.PositionX = pos.X;
        rider.PositionY = pos.Y;
        rider.PositionZ = pos.Z;
    }

}
