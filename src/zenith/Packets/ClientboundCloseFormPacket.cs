using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>ClientboundCloseForm (0x136) — server clears client form stack.</summary>
sealed class ClientboundCloseFormPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.CLIENTBOUND_CLOSE_FORM_PACKET;

    public override void Decode(ref BinaryStream stream) { }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        return writer.GetBufferDisposing();
    }
}
