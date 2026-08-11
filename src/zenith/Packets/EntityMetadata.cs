using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Keys / types / flags do entity metadata Bedrock (wire).</summary>
static class EntityMetaKey
{
    public const int Flags = 0;
    public const int Variant = 2;
    public const int ColorIndex = 3;
    public const int Name = 4;
    public const int EffectColor = 8;
    public const int EffectAmbience = 9;
    public const int Width = 53;
    public const int Height = 54;
    public const int AlwaysShowNameTag = 81;
}

static class EntityMetaType
{
    public const int Byte = 0;
    public const int Int = 2;
    public const int Float = 3;
    public const int String = 4;
    public const int Long = 7;
}

static class EntityFlag
{
    public const int Sneaking = 1;
    public const int Sprinting = 3;
    public const int ShowName = 14;
    public const int AlwaysShowName = 15;
    public const int CanClimb = 19;
    public const int Breathing = 35;
    public const int HasCollision = 48;
    public const int AffectedByGravity = 49;

    public static long Bit(int index) => 1L << index;
}

/// <summary>Shared metadata wire for AddPlayer / SetActorData (no domain types).</summary>
static class EntityMetadataWriter
{
    /// <summary>
    /// DataItemEntry.Payload at protocol 2168+ (ADR §82): a union discriminator (uvarint32, cases
    /// in DataItemType declaration order) followed by the chosen payload struct, whose own first
    /// field is that same DataItemType value again — a genuine double-write, confirmed against
    /// EndstoneMC/bedrock-protocol's compiler (VariantFieldGenerator writes the tag, then the
    /// struct's own fields including its "type" member) and the raw Mojang schema's per-variant
    /// "Type" field. Both writes use the same varint call: identical bytes to a raw uint8 for
    /// DataItemType's 0-9 range, so no width mismatch either way.
    /// </summary>
    private static void WriteEntryType(ref BinaryStream writer, int type)
    {
        writer.WriteUnsignedVarInt(type); // union discriminator
        writer.WriteUnsignedVarInt(type); // payload struct's own "type" field
    }

    /// <summary>Base spawn FLAGS (Breathing, collision, name) plus optional pose bits (§53).</summary>
    public static long BuildSpawnFlags(bool sneaking = false, bool sprinting = false)
    {
        long flags =
            EntityFlag.Bit(EntityFlag.Breathing) |
            EntityFlag.Bit(EntityFlag.CanClimb) |
            EntityFlag.Bit(EntityFlag.HasCollision) |
            EntityFlag.Bit(EntityFlag.AffectedByGravity) |
            EntityFlag.Bit(EntityFlag.ShowName) |
            EntityFlag.Bit(EntityFlag.AlwaysShowName);

        if (sneaking)
            flags |= EntityFlag.Bit(EntityFlag.Sneaking);
        if (sprinting)
            flags |= EntityFlag.Bit(EntityFlag.Sprinting);
        return flags;
    }

    /// <summary>FLAGS long for dirty pose updates (seed bits + sneak/sprint).</summary>
    public static long BuildPoseFlags(bool sneaking, bool sprinting) =>
        BuildSpawnFlags(sneaking, sprinting);

    /// <summary>FLAGS(+Breathing) + visible-name entries used by peer AddPlayer and local SetActorData.</summary>
    public static void WriteVisibleNameMetadata(
        ref BinaryStream writer,
        string name,
        bool sneaking = false,
        bool sprinting = false)
    {
        var flags = BuildSpawnFlags(sneaking, sprinting);

        writer.WriteUnsignedVarInt(8);

        writer.WriteUnsignedVarInt(EntityMetaKey.Flags);
        WriteEntryType(ref writer, EntityMetaType.Long);
        writer.WriteVarLong(flags);

        writer.WriteUnsignedVarInt(EntityMetaKey.ColorIndex);
        WriteEntryType(ref writer, EntityMetaType.Byte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.Name);
        WriteEntryType(ref writer, EntityMetaType.String);
        writer.WriteVarString(name);

        writer.WriteUnsignedVarInt(EntityMetaKey.EffectColor);
        WriteEntryType(ref writer, EntityMetaType.Int);
        writer.WriteVarInt(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.EffectAmbience);
        WriteEntryType(ref writer, EntityMetaType.Byte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.Width);
        WriteEntryType(ref writer, EntityMetaType.Float);
        writer.WriteFloat(0.6f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(EntityMetaKey.Height);
        WriteEntryType(ref writer, EntityMetaType.Float);
        writer.WriteFloat(1.8f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(EntityMetaKey.AlwaysShowNameTag);
        WriteEntryType(ref writer, EntityMetaType.Byte);
        writer.WriteByte(1);
    }

    /// <summary>Single FLAGS entry for runtime sneak/sprint dirty fan-out (§53).</summary>
    public static void WriteFlagsOnly(ref BinaryStream writer, long flags)
    {
        writer.WriteUnsignedVarInt(1);
        writer.WriteUnsignedVarInt(EntityMetaKey.Flags);
        WriteEntryType(ref writer, EntityMetaType.Long);
        writer.WriteVarLong(flags);
    }

    /// <summary>
    /// FLAGS(gravity, collision) + DATA_VARIANT — how the client picks which block texture to
    /// render for a falling_block actor (confirmed against PowerNukkitX's
    /// <c>EntityFallingBlock</c>: <c>setDataProperty(ActorDataTypes.VARIANT, blockState.blockStateHash())</c>
    /// — ADR §95). Name is falling_block-specific on purpose: the FLAGS bits it writes
    /// (<see cref="EntityFlag.AffectedByGravity"/> + <see cref="EntityFlag.HasCollision"/>) are
    /// falling_block's own semantics, not appropriate for every future DATA_VARIANT use — a
    /// second variant-carrying entity with different flags needs its own writer, not a
    /// generic-named one that quietly forces gravity on it.
    /// </summary>
    public static void WriteFallingBlockMetadata(ref BinaryStream writer, int variant)
    {
        writer.WriteUnsignedVarInt(2);

        writer.WriteUnsignedVarInt(EntityMetaKey.Flags);
        WriteEntryType(ref writer, EntityMetaType.Long);
        writer.WriteVarLong(EntityFlag.Bit(EntityFlag.AffectedByGravity) | EntityFlag.Bit(EntityFlag.HasCollision));

        writer.WriteUnsignedVarInt(EntityMetaKey.Variant);
        WriteEntryType(ref writer, EntityMetaType.Int);
        writer.WriteVarInt(variant);
    }

    /// <summary>Minimal metadata required for a client-rendered zombie actor.</summary>
    public static void WriteZombieMetadata(ref BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(3);
        writer.WriteUnsignedVarInt(EntityMetaKey.Flags);
        WriteEntryType(ref writer, EntityMetaType.Long);
        writer.WriteVarLong(BuildSpawnFlags());
        writer.WriteUnsignedVarInt(EntityMetaKey.Width);
        WriteEntryType(ref writer, EntityMetaType.Float);
        writer.WriteFloat(0.6f, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(EntityMetaKey.Height);
        WriteEntryType(ref writer, EntityMetaType.Float);
        writer.WriteFloat(1.95f, BinaryStream.Endianess.Little);
    }
}
