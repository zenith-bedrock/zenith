using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>
/// Replica pose absoluta de um actor aos peers.
/// Payload alinhado ao Bedrock: runtime id (unsigned varlong), flags (u8),
/// position (3× LE float), pitch/yaw/headYaw como rotation bytes (ângulo / 360 * 256).
/// Sem campo teleport-cause neste packet ID (0x12).
/// </summary>
class MoveActorAbsolutePacket : DataPacket
{
    public const byte FLAG_ON_GROUND = 1;

    public override int Id => (int)ProtocolInfo.MOVE_ACTOR_ABSOLUTE_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public byte Flags { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteByte(Flags);
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little);
        WriteRotationByte(ref writer, Pitch);
        WriteRotationByte(ref writer, Yaw);
        WriteRotationByte(ref writer, HeadYaw);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }

    private static void WriteRotationByte(ref BinaryStream writer, float degrees)
    {
        var encoded = (byte)((int)(degrees / (360.0f / 256.0f)) & 0xff);
        writer.WriteByte(encoded);
    }
}
