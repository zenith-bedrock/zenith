using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// SetActorData (0x27) — local spawn seed (§34) and peer pose FLAGS updates (§53).
/// </summary>
sealed class SetActorDataPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_ACTOR_DATA_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public string Name { get; set; } = "";
    public ulong Tick { get; set; }

    /// <summary>When true, Encode writes FLAGS-only (pose dirty); otherwise full visible-name seed.</summary>
    public bool FlagsOnly { get; set; }

    public bool Sneaking { get; set; }
    public bool Sprinting { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);

        if (FlagsOnly)
            EntityMetadataWriter.WriteFlagsOnly(
                ref writer,
                EntityMetadataWriter.BuildPoseFlags(Sneaking, Sprinting));
        else
            EntityMetadataWriter.WriteVisibleNameMetadata(ref writer, Name, Sneaking, Sprinting);

        writer.WriteUnsignedVarInt(0); // IntegerProperties empty
        writer.WriteUnsignedVarInt(0); // FloatProperties empty
        writer.WriteUnsignedVarLong((long)Tick);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
