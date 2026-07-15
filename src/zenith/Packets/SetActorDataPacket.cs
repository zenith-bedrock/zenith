using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// SetActorData (0x27) — local-player metadata seed at spawn (ADR §34).
/// Breathing flag clears stuck air-bubble HUD; not an authority vitals system.
/// </summary>
sealed class SetActorDataPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_ACTOR_DATA_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public string Name { get; set; } = "";
    public ulong Tick { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        EntityMetadataWriter.WriteVisibleNameMetadata(ref writer, Name);
        writer.WriteUnsignedVarInt(0); // IntegerProperties empty
        writer.WriteUnsignedVarInt(0); // FloatProperties empty
        writer.WriteUnsignedVarLong((long)Tick);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
