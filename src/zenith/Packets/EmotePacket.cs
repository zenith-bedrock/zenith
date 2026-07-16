using Zenith.Raknet.Stream;

namespace Zenith.Packets;

enum EmoteFlags : int
{
    SERVER,
    MUTE_CHAT
}

sealed class EmotePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.EMOTE_PACKET;

    public long ActorRuntimeId { get; set; }
    public string EmoteId { get; set; }
    public int TickLength { get; set; }
    public string XUID { get; set; }
    public string PlatformChatId { get; set; }
    public EmoteFlags Flags { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        ActorRuntimeId = stream.ReadVarLong();
        EmoteId = stream.ReadVarString();
        TickLength = stream.ReadVarInt();
        XUID = stream.ReadString();
        PlatformChatId = stream.ReadString();
        Flags = (EmoteFlags)stream.ReadVarInt();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteVarLong(ActorRuntimeId);
        writer.WriteVarString(EmoteId);
        writer.WriteVarInt(TickLength);
        writer.WriteVarString(XUID);
        writer.WriteVarString(PlatformChatId);
        writer.WriteVarInt((int)Flags);

        return writer.GetBufferDisposing();
    }
}
