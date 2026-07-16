using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>ModalFormRequest (0x64) — server → client open form (JSON body).</summary>
sealed class ModalFormRequestPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.MODAL_FORM_REQUEST_PACKET;

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
