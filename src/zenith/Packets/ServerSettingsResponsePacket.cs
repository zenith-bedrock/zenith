using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>ServerSettingsResponse (0x67) — server → client settings tab (same shape as ModalFormRequest).</summary>
[GamePacket((int)ProtocolInfo.SERVER_SETTINGS_RESPONSE_PACKET)]
sealed partial class ServerSettingsResponsePacket : DataPacket
{
    [WireVar]
    public uint FormId { get; set; }

    [WireString]
    public string FormUiJson { get; set; } = "";
}
