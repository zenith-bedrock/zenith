using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>Emote (0x8a) — client ↔ server. Flags are a bitfield byte (server-side, mute chat, …).</summary>
[GamePacket((int)ProtocolInfo.EMOTE_PACKET)]
sealed partial class EmotePacket : DataPacket
{
    public const byte FlagServerSide = 1 << 0;
    public const byte FlagMuteChat = 1 << 1;

    [WireVar]
    public ulong ActorRuntimeId { get; set; }

    [WireString]
    public string EmoteId { get; set; } = "";

    [WireVar]
    public uint TickLength { get; set; }

    [WireString]
    public string Xuid { get; set; } = "";

    [WireString]
    public string PlatformChatId { get; set; } = "";

    [Wire]
    public byte Flags { get; set; }
}
