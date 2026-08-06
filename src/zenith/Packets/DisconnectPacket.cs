using Zenith.Packets.Generation;

namespace Zenith.Packets;

[GamePacket((int)ProtocolInfo.DISCONNECT_PACKET)]
sealed partial class DisconnectPacket : DataPacket
{
    /// <summary>Bedrock DisconnectFailReason base (iota 0). More values when outbound kick needs them.</summary>
    public const int ReasonUnknown = 0;

    [WireVar]
    public int Reason { get; set; }

    [Wire]
    public bool HideDisconnectionScreen { get; set; }

    [WireString]
    [WireWhen(nameof(HideDisconnectionScreen), false)]
    public string Message { get; set; } = "";

    [WireString]
    [WireWhen(nameof(HideDisconnectionScreen), false)]
    public string FilteredMessage { get; set; } = "";
}
