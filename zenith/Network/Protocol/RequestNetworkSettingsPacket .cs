using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class RequestNetworkSettingsPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.REQUEST_NETWORK_SETTINGS_PACKET;

    public int ProtocolVersion { get; set; }

    public override void Decode(BinaryStream stream)
    {
        ProtocolVersion = stream.ReadInt();
    }
}