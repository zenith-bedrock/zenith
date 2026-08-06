using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>ToastRequest (0xba) — server → client top-of-screen toast.</summary>
[GamePacket((int)ProtocolInfo.TOAST_REQUEST_PACKET)]
sealed partial class ToastRequestPacket : DataPacket
{
    [WireString]
    public string Title { get; set; } = "";

    [WireString]
    public string Content { get; set; } = "";
}
