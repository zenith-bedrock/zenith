using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>ContainerClose (0x2f).</summary>
sealed class ContainerClosePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.CONTAINER_CLOSE_PACKET;

    public byte WindowId { get; set; }
    public byte WindowType { get; set; }
    public bool ServerInitiated { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteByte(WindowId);
        writer.WriteByte(WindowType);
        writer.WriteBool(ServerInitiated);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        WindowId = stream.ReadByte();
        WindowType = stream.ReadByte();
        ServerInitiated = stream.ReadBool();
    }
}
