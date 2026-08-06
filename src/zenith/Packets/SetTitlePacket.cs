using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>SetTitle (0x58) — server → client title/subtitle/actionbar UI banners.</summary>
[GamePacket((int)ProtocolInfo.SET_TITLE_PACKET)]
sealed partial class SetTitlePacket : DataPacket
{
    public enum TitleType : int
    {
        Clear = 0,
        Reset = 1,
        Title = 2,
        Subtitle = 3,
        Actionbar = 4,
        Times = 5,
        TitleTextObject = 6,
        SubtitleTextObject = 7,
        ActionbarTextObject = 8,
    }

    [WireVar]
    public TitleType Type { get; set; }

    [WireString]
    public string TitleText { get; set; } = "";

    [WireVar]
    public int FadeInTime { get; set; }

    [WireVar]
    public int StayTime { get; set; }

    [WireVar]
    public int FadeOutTime { get; set; }

    [WireString]
    public string Xuid { get; set; } = "";

    [WireString]
    public string PlatformOnlineId { get; set; } = "";

    [WireString]
    public string FilteredTitleMessage { get; set; } = "";
}
