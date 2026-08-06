using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>ModalFormResponse (0x65) — client → server. Cancel reason is optional byte.</summary>
[GamePacket((int)ProtocolInfo.MODAL_FORM_RESPONSE_PACKET)]
sealed partial class ModalFormResponsePacket : DataPacket
{
    public const byte CancelUserClosed = 0;
    public const byte CancelUserBusy = 1;

    [WireVar]
    public uint FormId { get; set; }

    [WireString]
    [WireOptional]
    public string? FormUiJson { get; set; }

    [Wire]
    [WireOptional]
    public byte? CancelReason { get; set; }
}
