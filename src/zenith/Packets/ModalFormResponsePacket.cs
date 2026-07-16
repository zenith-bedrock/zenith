using Zenith.Raknet.Stream;

namespace Zenith.Packets;

enum ModalFormResponseType : byte
{
    USER_CLOSED,
    USER_BUSY
}

sealed class ModalFormResponsePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.MODAL_FORM_RESPONSE_PACKET;

    public int FormId { get; set; }
    public string? FormUiJson { get; set; }
    public ModalFormResponseType? ResponseType { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        FormId = stream.ReadUnsignedVarInt();

        if (stream.ReadBool())
            FormUiJson = stream.ReadVarString();

        if (stream.ReadBool())
            ResponseType = (ModalFormResponseType)stream.ReadVarInt();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteUnsignedVarInt(FormId);

        if (FormUiJson != null)
        {
            writer.WriteBool(true);
            writer.WriteVarString(FormUiJson ?? string.Empty);
        }
        else
        {
            writer.WriteBool(false);
        }

        if (ResponseType != null)
        {
            writer.WriteBool(true);
            writer.WriteVarInt((int)ResponseType);
        }
        else
        {
            writer.WriteBool(false);
        }

        return writer.GetBufferDisposing();
    }
}
