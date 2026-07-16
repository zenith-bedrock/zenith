using Zenith.Raknet.Stream;

namespace Zenith.Packets;

sealed class ServerSettingsRequestPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SERVER_SETTINGS_REQUEST_PACKET;

    public override void Decode(ref BinaryStream stream)
    {
    }
}
