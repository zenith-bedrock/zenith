using Zenith.Raknet.Stream;

namespace Zenith.Packets;

sealed class ClientboundCloseFormPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.CLIENTBOUND_CLOSE_FORM_PACKET;

    public override void Decode(ref BinaryStream stream)
    {
    }
}
