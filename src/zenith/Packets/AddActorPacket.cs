using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// AddActor (0x0d) — generic non-player entity spawn (outbound). Not AddPlayer/AddItemActor.
/// Field order cross-checked against gophertunnel's current (protocol 2168) <c>Marshal</c> and
/// endstone-bedrock-protocol's since=2168 shape — both agree byte-for-byte (ADR §95).
/// </summary>
sealed class AddActorPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.ADD_ACTOR_PACKET;

    public long EntityUniqueId { get; set; }
    public ulong EntityRuntimeId { get; set; }
    public string EntityType { get; set; } = "";
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }
    public float BodyYaw { get; set; }

    /// <summary>DATA_VARIANT metadata (e.g. falling_block's block runtime id) — null omits the entry.</summary>
    public int? Variant { get; set; }
    public bool ZombieMetadata { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarLong(EntityUniqueId);
        writer.WriteUnsignedVarLong((long)EntityRuntimeId);
        writer.WriteVarString(EntityType);
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityX, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityY, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityZ, BinaryStream.Endianess.Little);
        writer.WriteFloat(Pitch, BinaryStream.Endianess.Little);
        writer.WriteFloat(Yaw, BinaryStream.Endianess.Little);
        writer.WriteFloat(HeadYaw, BinaryStream.Endianess.Little);
        writer.WriteFloat(BodyYaw, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0); // Attributes — empty (Zenith sends none for falling_block)
        if (ZombieMetadata)
            EntityMetadataWriter.WriteZombieMetadata(ref writer);
        else if (Variant is { } variant)
            EntityMetadataWriter.WriteFallingBlockMetadata(ref writer, variant);
        else
            writer.WriteUnsignedVarInt(0); // EntityMetadata — empty
        writer.WriteUnsignedVarInt(0); // EntityProperties.int list — empty
        writer.WriteUnsignedVarInt(0); // EntityProperties.float list — empty
        writer.WriteUnsignedVarInt(0); // EntityLinks — empty
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
