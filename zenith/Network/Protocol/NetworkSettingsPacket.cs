using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class NetworkSettingsPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.NETWORK_SETTINGS_PACKET;

    public const byte COMPRESS_NOTHING = 0;
    public const byte COMPRESS_EVERYTHING = 1;

    public short CompressionThreshold { get; set; }
    public short CompressionAlgorithm { get; set; }
    public bool EnableClientThrottling { get; set; }
    public byte ClientThrottleThreshold { get; set; }
    public float ClientThrottleScalar { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteShort(CompressionThreshold, BinaryStream.Endianess.Little);
        writer.WriteShort(CompressionAlgorithm, BinaryStream.Endianess.Little);
        writer.WriteBool(EnableClientThrottling);
        writer.WriteByte(ClientThrottleThreshold);
        writer.WriteFloat(ClientThrottleScalar, BinaryStream.Endianess.Little);
        return writer.GetBufferDisposing();
    }

    public override void Decode(BinaryStream stream)
    {
        CompressionThreshold = stream.ReadShort(BinaryStream.Endianess.Little);
        CompressionAlgorithm = stream.ReadShort(BinaryStream.Endianess.Little);
        EnableClientThrottling = stream.ReadBool();
        ClientThrottleThreshold = stream.ReadByte();
        ClientThrottleScalar = stream.ReadFloat(BinaryStream.Endianess.Little);
    }
}