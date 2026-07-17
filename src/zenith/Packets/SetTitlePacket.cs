using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>SetTitle (0x58) — server → client title/subtitle/actionbar UI banners.</summary>
sealed class SetTitlePacket : DataPacket
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

    public override int Id => (int)ProtocolInfo.SET_TITLE_PACKET;

    public TitleType Type { get; set; }
    public string TitleText { get; set; } = "";
    public int FadeInTime { get; set; }
    public int StayTime { get; set; }
    public int FadeOutTime { get; set; }
    public string Xuid { get; set; } = "";
    public string PlatformOnlineId { get; set; } = "";
    public string FilteredTitleMessage { get; set; } = "";

    public override void Decode(ref BinaryStream stream)
    {
        Type = (TitleType)stream.ReadVarInt();
        TitleText = stream.ReadVarString();
        FadeInTime = stream.ReadVarInt();
        StayTime = stream.ReadVarInt();
        FadeOutTime = stream.ReadVarInt();
        Xuid = stream.ReadVarString();
        PlatformOnlineId = stream.ReadVarString();
        FilteredTitleMessage = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt((int)Type);
        writer.WriteVarString(TitleText);
        writer.WriteVarInt(FadeInTime);
        writer.WriteVarInt(StayTime);
        writer.WriteVarInt(FadeOutTime);
        writer.WriteVarString(Xuid);
        writer.WriteVarString(PlatformOnlineId);
        writer.WriteVarString(FilteredTitleMessage);
        return writer.GetBufferDisposing();
    }
}
