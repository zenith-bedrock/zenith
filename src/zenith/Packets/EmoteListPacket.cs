using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>EmoteList (0x98) — client (join/equip) ± rare server. Cap protects decode DoS.</summary>
sealed class EmoteListPacket : DataPacket
{
    public const int MaxEmotes = 64;

    public override int Id => (int)ProtocolInfo.EMOTE_LIST_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public Guid[] Emotes { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteUnsignedVarInt(Emotes.Length);
        foreach (var emote in Emotes)
            writer.WriteUuid(emote);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        ActorRuntimeId = (ulong)stream.ReadUnsignedVarLong();
        var count = stream.ReadUnsignedVarInt();
        if (count > MaxEmotes)
            throw new InvalidDataException($"EmoteList count {count} exceeds {MaxEmotes}.");
        Emotes = new Guid[count];
        for (var i = 0; i < count; i++)
            Emotes[i] = stream.ReadUuid();
    }
}
