using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct EmoteData
{
    public string UUID { get; init; }
}

sealed class EmoteListPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.EMOTE_LIST_PACKET;

    public long ActorRuntimeId { get; set; }
    public EmoteData[] Emotes { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteVarLong(ActorRuntimeId);
        writer.WriteVarLong(Emotes.Length);
        foreach (var emote in Emotes)
        {
            writer.WriteUuid(Guid.Parse(emote.UUID));
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        var emoteAmount = stream.ReadVarInt();
        Emotes = new EmoteData[emoteAmount];
        for (var i = 0; i < emoteAmount; i++)
        {
            Emotes[i] = new EmoteData
            {
                UUID = stream.ReadUuid().ToString()
            };
        }
    }
}
