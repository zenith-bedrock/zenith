using Zenith.Packets.Generation;

namespace Zenith.Packets;

[GamePacket((int)ProtocolInfo.RESOURCE_PACK_CLIENT_RESPONSE_PACKET)]
sealed partial class ResourcePackClientResponsePacket : DataPacket
{
    public const byte STATUS_REFUSED = 1;
    public const byte STATUS_SEND_PACKS = 2;
    public const byte STATUS_HAVE_ALL_PACKS = 3;
    public const byte STATUS_COMPLETED = 4;

    // TODO: decode pack ids

    [Wire]
    public byte Status { get; set; }
}
