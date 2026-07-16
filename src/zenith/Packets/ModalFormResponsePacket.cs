using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>ModalFormResponse (0x65) — client → server. Cancel reason is optional byte.</summary>
sealed class ModalFormResponsePacket : DataPacket
{
    public const byte CancelUserClosed = 0;
    public const byte CancelUserBusy = 1;

    public override int Id => (int)ProtocolInfo.MODAL_FORM_RESPONSE_PACKET;

    public uint FormId { get; set; }
    public string? FormUiJson { get; set; }
    public byte? CancelReason { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        FormId = (uint)stream.ReadUnsignedVarInt();
        FormUiJson = stream.ReadBool() ? stream.ReadVarString() : null;
        CancelReason = stream.ReadBool() ? stream.ReadByte() : null;
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarInt((int)FormId);
        if (FormUiJson is not null)
        {
            writer.WriteBool(true);
            writer.WriteVarString(FormUiJson);
        }
        else
        {
            writer.WriteBool(false);
        }

        if (CancelReason is { } reason)
        {
            writer.WriteBool(true);
            writer.WriteByte(reason);
        }
        else
        {
            writer.WriteBool(false);
        }

        return writer.GetBufferDisposing();
    }
}
