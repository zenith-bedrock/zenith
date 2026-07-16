using Zenith.Raknet.Stream;

namespace Zenith.Packets;

sealed class ServerSettingsResponsePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SERVER_SETTINGS_RESPONSE_PACKET;

    public int FormId { get; set; }
    public string FormUiJson { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        FormId = stream.ReadUnsignedVarInt();
        FormUiJson = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteUnsignedVarInt(FormId);
        writer.WriteVarString(FormUiJson);

        return writer.GetBufferDisposing();
    }
}
