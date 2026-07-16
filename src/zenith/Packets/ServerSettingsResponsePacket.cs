using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>ServerSettingsResponse (0x67) — server → client settings tab (same shape as ModalFormRequest).</summary>
sealed class ServerSettingsResponsePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SERVER_SETTINGS_RESPONSE_PACKET;

    public uint FormId { get; set; }
    public string FormUiJson { get; set; } = "";

    public override void Decode(ref BinaryStream stream)
    {
        FormId = (uint)stream.ReadUnsignedVarInt();
        FormUiJson = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarInt((int)FormId);
        writer.WriteVarString(FormUiJson);
        return writer.GetBufferDisposing();
    }
}
