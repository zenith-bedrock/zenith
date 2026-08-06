using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>ModalFormRequest (0x64) — server → client open form (JSON body).</summary>
[GamePacket((int)ProtocolInfo.MODAL_FORM_REQUEST_PACKET)]
sealed partial class ModalFormRequestPacket : DataPacket
{
    [WireVar]
    public uint FormId { get; set; }

    [WireString]
    public string FormUiJson { get; set; } = "";
}
