using Zenith.Packets.Generation;

namespace Zenith.Packets;

[GamePacket((int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET)]
sealed partial class RequestNetworkSettingsPacket : DataPacket
{
    [Wire]
    public int ProtocolVersion { get; set; }
}
