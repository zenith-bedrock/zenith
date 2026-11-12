using Zenith.Ecs;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// How a walking actor moves through the world: one step if the terrain allows it, falling and landing, and the plain
/// "walk toward a player, sliding around obstacles" chase with the shared target-retention rule. These were copied
/// verbatim into Zombie, Spider, Golem, Skeleton, Creeper and Cow; a species keeps only its decisions (when to chase,
/// how far, how fast) and passes them in. Terrain validity itself stays in <see cref="GroundMobMovement"/>.
/// </summary>
sealed class GroundMobBody(World.World world, EntityRuntime stores, Player.PlayerManager players)
{
    /// <summary>Moves the actor to (<paramref name="x"/>, <paramref name="z"/>) if the terrain lets it; false (and no change) otherwise.</summary>
    public bool TryMove(EntityId id, float x, float z)
    {
        if (!stores.Positions.TryGet(id, out var pos)) return false;
        if (!GroundMobMovement.TryMoveHorizontal(world, pos.X, pos.Y, pos.Z, x, z, out var resolvedY)) return false;
        ref var p = ref stores.Positions.GetRef(id);
        p.X = x;
        p.Y = resolvedY;
        p.Z = z;
        return true;
    }

    /// <summary>One tick of gravity, falling and landing; <see cref="Velocity.Y"/> is the downward fall speed.</summary>
    public void ApplyGravity(EntityId id)
    {
        if (!stores.Positions.TryGet(id, out var pos)) return;
        ref var vel = ref stores.Velocities.GetRef(id);
        var y = pos.Y;
        if (!GroundMobMovement.ResolveVertical(world, pos.X, pos.Z, ref y, ref vel.Y, out _)) return;
        ref var p = ref stores.Positions.GetRef(id);
        p.Y = y;
    }

    /// <summary>True when <paramref name="target"/> is in the game, alive and within <paramref name="detectionDistance"/>; also yields the squared distance.</summary>
    public static bool IsTargetValid(Position pos, Player.Player target, float detectionDistance, out float distanceSquared)
    {
        var dx = target.PositionX - pos.X;
        var dz = target.PositionZ - pos.Z;
        distanceSquared = dx * dx + dz * dz;
        return target.IsInGame && !target.IsDead && distanceSquared <= detectionDistance * detectionDistance;
    }

    /// <summary>
    /// Keeps the current target while it stays valid, otherwise picks the nearest valid player (or none). The retained id lives
    /// in the species' state component, so it is passed by reference.
    /// </summary>
    public Player.Player? FindOrAcquireTarget(
        EntityId id, IReadOnlyList<Player.Player> online, ref long? retainedTargetRuntimeId, float detectionDistance)
    {
        if (!stores.Positions.TryGet(id, out var pos)) return null;

        if (retainedTargetRuntimeId is { } retainedId)
        {
            var retained = players.GetByRuntimeId(retainedId);
            if (retained is not null && IsTargetValid(pos, retained, detectionDistance, out _))
                return retained;
            retainedTargetRuntimeId = null;
        }

        Player.Player? target = null;
        var best = detectionDistance * detectionDistance;
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

        retainedTargetRuntimeId = target?.RuntimeId;
        return target;
    }

    /// <summary>
    /// One step toward <paramref name="target"/>, stopping at <paramref name="stopDistance"/>: straight first, then sliding to either
    /// side when blocked; turns the actor toward the way it moved.
    /// </summary>
    public void ChaseTarget(EntityId id, Player.Player target, float detectionDistance, float stopDistance, float movePerTick)
    {
        if (!stores.Positions.TryGet(id, out var pos)) return;
        if (!IsTargetValid(pos, target, detectionDistance, out var best) ||
            best <= stopDistance * stopDistance || best <= 0.0001f)
            return;

        var length = MathF.Sqrt(best);
        var dxn = (target.PositionX - pos.X) / length;
        var dzn = (target.PositionZ - pos.Z) / length;
        var distance = MathF.Min(movePerTick, length - stopDistance);
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
            ref var p = ref stores.Positions.GetRef(id);
            p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(dxn, dzn), LookMath.DefaultMaxTurnDegreesPerTick);
        }
    }
}
