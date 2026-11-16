using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Where actors are: absolute moves (single and batched), raw non-player moves, the player teleport.</summary>
sealed partial class EntityProtocol
{
    /// <param name="y">Domain feet Y — wire adds <see cref="EntityHitboxes.PlayerNetworkOffset"/>.</param>
    public void SendMoveAbsolute(
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        byte flags = 0)
    {
        _session.SendDataPacket(CreateMoveAbsolute(actorRuntimeId, x, y, z, pitch, yaw, headYaw, flags));
    }

    /// <summary>One GamePacket envelope for N Absolute moves (ADR §44).</summary>
    public void SendMoveAbsolutes(IReadOnlyList<AbsoluteActorPose> poses)
    {
        if (poses.Count == 0) return;
        if (poses.Count == 1)
        {
            var p = poses[0];
            SendMoveAbsolute(
                p.ActorRuntimeId, p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.HeadYaw,
                p.OnGround ? MoveActorAbsolutePacket.FLAG_ON_GROUND : (byte)0);
            return;
        }

        var packets = new DataPacket[poses.Count];
        for (var i = 0; i < poses.Count; i++)
        {
            var p = poses[i];
            packets[i] = CreateMoveAbsolute(
                p.ActorRuntimeId, p.X, p.Y, p.Z, p.Pitch, p.Yaw, p.HeadYaw,
                p.OnGround ? MoveActorAbsolutePacket.FLAG_ON_GROUND : (byte)0);
        }

        _session.SendDataPacket(packets);
    }

    /// <summary>
    /// <paramref name="y"/> is domain feet; packet Y is <see cref="EntityHitboxes.AbsoluteWireY"/>.
    /// </summary>
    private static MoveActorAbsolutePacket CreateMoveAbsolute(
        ulong actorRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        byte flags = 0) =>
        new()
        {
            ActorRuntimeId = actorRuntimeId,
            Flags = flags,
            PositionX = x,
            PositionY = EntityHitboxes.AbsoluteWireY(y),
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw
        };

    /// <summary>
    /// Non-player actor move — no <see cref="EntityHitboxes.AbsoluteWireY"/> eye offset (that's a
    /// player-only wire convention). <paramref name="y"/> is the actor's own base position (ADR §95).
    /// </summary>
    public void SendMoveActorAbsoluteRaw(
        ulong actorRuntimeId, float x, float y, float z, byte flags = 0, float pitch = 0f, float yaw = 0f, float headYaw = 0f)
    {
        _session.SendDataPacket(new MoveActorAbsolutePacket
        {
            ActorRuntimeId = actorRuntimeId,
            Flags = flags,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw
        });
    }

    /// <summary>
    /// Local camera snap — MovePlayer Teleport (ADR §41). Not for peers.
    /// <paramref name="y"/> is domain feet; packet Y is <see cref="EntityHitboxes.AbsoluteWireY"/>.
    /// </summary>
    public void SendMovePlayerTeleport(
        ulong entityRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        ulong tick = 0)
    {
        _session.SendDataPacket(MovePlayerPacket.CreateTeleport(
            entityRuntimeId, x, EntityHitboxes.AbsoluteWireY(y), z, pitch, yaw, headYaw, tick));
    }

    /// <summary>One transport submission for several raw non-player actor movements.</summary>
    public void SendMoveActorAbsoluteRaws(IReadOnlyList<RawActorPose> poses)
    {
        if (poses.Count == 0) return;
        if (poses.Count == 1)
        {
            var p = poses[0];
            SendMoveActorAbsoluteRaw(p.ActorRuntimeId, p.X, p.Y, p.Z, p.Flags, p.Pitch, p.Yaw, p.HeadYaw);
            return;
        }

        var packets = new MoveActorAbsolutePacket[poses.Count];
        for (var i = 0; i < poses.Count; i++)
        {
            var p = poses[i];
            packets[i] = new MoveActorAbsolutePacket
            {
                ActorRuntimeId = p.ActorRuntimeId,
                Flags = p.Flags,
                PositionX = p.X,
                PositionY = p.Y,
                PositionZ = p.Z,
                Pitch = p.Pitch,
                Yaw = p.Yaw,
                HeadYaw = p.HeadYaw
            };
        }
        _session.SendDataPacket(packets);
    }
}
