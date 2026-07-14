using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

class PlayStatusPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.PLAY_STATUS_PACKET;

    public int Status { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteInt(Status);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}