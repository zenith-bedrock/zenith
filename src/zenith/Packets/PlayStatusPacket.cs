using Zenith.Raknet.Stream;

namespace Zenith.Packets;

class PlayStatusPacket : DataPacket
{
    public const int LoginSuccess = 0;
    public const int LoginFailedClient = 1; // client protocol < server
    public const int LoginFailedServer = 2; // client protocol > server
    public const int PlayerSpawn = 3;

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
