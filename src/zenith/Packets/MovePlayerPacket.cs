using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// MovePlayer (0x13) — local player pose correction / teleport (ADR §41).
/// Outbound Teleport mode snaps client camera; peers still use MoveActorAbsolute.
/// </summary>
sealed class MovePlayerPacket : DataPacket
{
    public const byte ModeNormal = 0;
    public const byte ModeReset = 1;
    public const byte ModeTeleport = 2;
    public const byte ModeRotation = 3;

    public const int TeleportCauseUnknown = 0;
    public const int TeleportCauseProjectile = 1;
    public const int TeleportCauseChorusFruit = 2;
    public const int TeleportCauseCommand = 3;
    public const int TeleportCauseBehaviour = 4;

    public const int TeleportSourceEntityTypeNone = 0;

    public override int Id => (int)ProtocolInfo.MOVE_PLAYER_PACKET;

    public ulong EntityRuntimeId { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }
    public byte Mode { get; set; } = ModeTeleport;
    public bool OnGround { get; set; } = true;
    public ulong RiddenEntityRuntimeId { get; set; }
    public int TeleportCause { get; set; } = TeleportCauseCommand;
    public int TeleportSourceEntityType { get; set; }
    public ulong Tick { get; set; }

    public static MovePlayerPacket CreateTeleport(
        ulong entityRuntimeId,
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float headYaw,
        ulong tick = 0) =>
        new()
        {
            EntityRuntimeId = entityRuntimeId,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Pitch = pitch,
            Yaw = yaw,
            HeadYaw = headYaw,
            Mode = ModeTeleport,
            OnGround = true,
            TeleportCause = TeleportCauseCommand,
            TeleportSourceEntityType = TeleportSourceEntityTypeNone,
            Tick = tick
        };

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)EntityRuntimeId);
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little);
        writer.WriteFloat(Pitch, BinaryStream.Endianess.Little);
        writer.WriteFloat(Yaw, BinaryStream.Endianess.Little);
        writer.WriteFloat(HeadYaw, BinaryStream.Endianess.Little);
        writer.WriteByte(Mode);
        writer.WriteBool(OnGround);
        writer.WriteUnsignedVarLong((long)RiddenEntityRuntimeId);
        if (Mode == ModeTeleport)
        {
            writer.WriteInt(TeleportCause, BinaryStream.Endianess.Little);
            writer.WriteInt(TeleportSourceEntityType, BinaryStream.Endianess.Little);
        }

        writer.WriteUnsignedVarLong((long)Tick);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
