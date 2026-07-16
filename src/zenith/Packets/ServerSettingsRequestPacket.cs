using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>ServerSettingsRequest (0x66) — client → server (empty body).</summary>
sealed class ServerSettingsRequestPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SERVER_SETTINGS_REQUEST_PACKET;

    public override void Decode(ref BinaryStream stream) { }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        return writer.GetBufferDisposing();
    }
}
