using Zenith.Raknet.Stream;

namespace Zenith.Network.Protocol;

class RequestNetworkSettingsPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET;

    public int ProtocolVersion { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        ProtocolVersion = stream.ReadInt();
    }
}