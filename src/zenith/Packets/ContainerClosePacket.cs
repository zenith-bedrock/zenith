using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>ContainerClose (0x2f).</summary>
[GamePacket((int)ProtocolInfo.CONTAINER_CLOSE_PACKET)]
sealed partial class ContainerClosePacket : DataPacket
{
    [Wire]
    public byte WindowId { get; set; }

    [Wire]
    public byte WindowType { get; set; }

    [Wire]
    public bool ServerInitiated { get; set; }
}
