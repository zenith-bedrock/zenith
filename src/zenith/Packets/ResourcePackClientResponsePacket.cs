using Zenith.Raknet.Stream;

namespace Zenith.Packets;

class ResourcePackClientResponsePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET;

    public const byte STATUS_REFUSED = 1;
    public const byte STATUS_SEND_PACKS = 2;
    public const byte STATUS_HAVE_ALL_PACKS = 3;
    public const byte STATUS_COMPLETED = 4;

    public byte Status { get; set; }

    // TODO: decode pack ids

    public override void Decode(ref BinaryStream stream)
    {
        Status = stream.ReadByte();
    }
}