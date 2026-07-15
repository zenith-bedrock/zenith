using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>LevelEvent (0x19) — particles / sounds / block crack feedback.</summary>
sealed class LevelEventPacket : DataPacket
{
    public const int EventStartBlockCracking = 3600;
    public const int EventStopBlockCracking = 3601;
    public const int EventBlockBreakSpeed = 3602;

    public override int Id => (int)ProtocolInfo.LEVEL_EVENT_PACKET;

    public int EventType { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int EventData { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(EventType);
        writer.WriteFloat(X, BinaryStream.Endianess.Little);
        writer.WriteFloat(Y, BinaryStream.Endianess.Little);
        writer.WriteFloat(Z, BinaryStream.Endianess.Little);
        writer.WriteVarInt(EventData);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
