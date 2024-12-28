using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class ResourcePackClientResponsePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET;

    public const byte STATUS_REFUSED = 1;
    public const byte STATUS_SEND_PACKS = 2;
    public const byte STATUS_HAVE_ALL_PACKS = 3;
    public const byte STATUS_COMPLETED = 4;

    public byte Status { get; set; }

    // TODO: decode pack ids

    public override void Decode(BinaryStream stream)
    {
        Status = stream.ReadByte();
    }
}