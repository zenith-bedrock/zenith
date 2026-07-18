using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Emote (0x8a) — client ↔ server. Flags are a bitfield byte (server-side, mute chat, …).</summary>
sealed class EmotePacket : DataPacket
{
    public const byte FlagServerSide = 1 << 0;
    public const byte FlagMuteChat = 1 << 1;

    public override int Id => (int)ProtocolInfo.EMOTE_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public string EmoteId { get; set; } = "";
    public uint TickLength { get; set; }
    public string Xuid { get; set; } = "";
    public string PlatformChatId { get; set; } = "";
    public byte Flags { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        ActorRuntimeId = (ulong)stream.ReadUnsignedVarLong();
        EmoteId = stream.ReadVarString();
        TickLength = (uint)stream.ReadUnsignedVarInt();
        Xuid = stream.ReadVarString();
        PlatformChatId = stream.ReadVarString();
        Flags = stream.ReadByte();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteVarString(EmoteId);
        writer.WriteUnsignedVarInt((int)TickLength);
        writer.WriteVarString(Xuid);
        writer.WriteVarString(PlatformChatId);
        writer.WriteByte(Flags);
        return writer.GetBufferDisposing();
    }
}
