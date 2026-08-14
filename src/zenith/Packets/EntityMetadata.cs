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
    public const int Scale = 38;
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
    /// <summary>Creeper charged-by-lightning state — confirmed against bedrock-protocol's <c>EntityMetadataFlags::POWERED</c> and gophertunnel's <c>EntityDataFlagPowered</c>.</summary>
    public const int Powered = 9;
    /// <summary>Creeper fuse-lit state — drives the vanilla swell/flash animation. Same sources as <see cref="Powered"/>.</summary>
    public const int Ignited = 10;
    public const int ShowName = 14;
    public const int AlwaysShowName = 15;
    public const int CanClimb = 19;
    public const int Resting = 23;
    public const int Breathing = 35;
    public const int HasCollision = 48;
    public const int AffectedByGravity = 49;

    public static long Bit(int index) => 1L << index;
}

/// <summary>
/// Phase XXIII-B DX fix — an entity-metadata "dictionary" section always leads with an entry count,
/// which every writer in this file used to hand-type as a literal that had to be kept in sync by eye
/// with however many entries actually followed. That mismatch is exactly what caused a real
/// client-crashing bug (found via live testing): the count said 3, four entries were written, and
/// every packet after the first mob using it was silently misparsed until the client gave up and
/// disconnected.
///
/// This builder makes that class of bug structurally impossible: the count written is always
/// <c>entries.Count</c> — the real number of builder calls made — never a literal. Same shape as
/// PocketMine's <c>EntityMetadataCollection</c> (confirmed against bedrock-protocol's
/// <c>CommonTypes::putEntityMetadata</c>: <c>VarInt::writeUnsignedInt($out, count($metadata))</c>),
/// adapted to Zenith's write-once spawn-metadata use — no dirty-tracking, no property lookup by key,
/// just "add exactly the entries this actor needs, then write them, count included for free."
/// </summary>
sealed class EntityMetadataBuilder
{
    private readonly struct Entry
    {
        public required int Key { get; init; }
        public required int Type { get; init; }
        public long LongValue { get; init; }
        public float FloatValue { get; init; }
        public string? StringValue { get; init; }
    }

    private readonly List<Entry> _entries = [];

    public EntityMetadataBuilder Flags(long flags)
    {
        _entries.Add(new Entry { Key = EntityMetaKey.Flags, Type = EntityMetaType.Long, LongValue = flags });
        return this;
    }

    public EntityMetadataBuilder Scale(float scale = 1f)
    {
        _entries.Add(new Entry { Key = EntityMetaKey.Scale, Type = EntityMetaType.Float, FloatValue = scale });
        return this;
    }

    public EntityMetadataBuilder Width(float width)
    {
        _entries.Add(new Entry { Key = EntityMetaKey.Width, Type = EntityMetaType.Float, FloatValue = width });
        return this;
    }

    public EntityMetadataBuilder Height(float height)
    {
        _entries.Add(new Entry { Key = EntityMetaKey.Height, Type = EntityMetaType.Float, FloatValue = height });
        return this;
    }

    /// <summary>Convenience for the common Width-then-Height pair.</summary>
    public EntityMetadataBuilder Dimensions(float width, float height) => Width(width).Height(height);

    public EntityMetadataBuilder Variant(int variant)
    {
        _entries.Add(new Entry { Key = EntityMetaKey.Variant, Type = EntityMetaType.Int, LongValue = variant });
        return this;
    }

    public EntityMetadataBuilder Int(int key, int value)
    {
        _entries.Add(new Entry { Key = key, Type = EntityMetaType.Int, LongValue = value });
        return this;
    }

    public EntityMetadataBuilder Byte(int key, byte value)
    {
        _entries.Add(new Entry { Key = key, Type = EntityMetaType.Byte, LongValue = value });
        return this;
    }

    public EntityMetadataBuilder String(int key, string value)
    {
        _entries.Add(new Entry { Key = key, Type = EntityMetaType.String, StringValue = value });
        return this;
    }

    public void WriteTo(ref BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(_entries.Count);
        foreach (var entry in _entries)
        {
            writer.WriteUnsignedVarInt(entry.Key);
            WriteEntryType(ref writer, entry.Type);
            switch (entry.Type)
            {
                case EntityMetaType.Long:
                    writer.WriteVarLong(entry.LongValue);
                    break;
                case EntityMetaType.Int:
                    writer.WriteVarInt((int)entry.LongValue);
                    break;
                case EntityMetaType.Byte:
                    writer.WriteByte((byte)entry.LongValue);
                    break;
                case EntityMetaType.Float:
                    writer.WriteFloat(entry.FloatValue, BinaryStream.Endianess.Little);
                    break;
                case EntityMetaType.String:
                    writer.WriteVarString(entry.StringValue!);
                    break;
            }
        }
    }

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
}

/// <summary>Shared metadata wire for AddPlayer / SetActorData (no domain types).</summary>
static class EntityMetadataWriter
{
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
        new EntityMetadataBuilder()
            .Flags(BuildSpawnFlags(sneaking, sprinting))
            .Byte(EntityMetaKey.ColorIndex, 0)
            .String(EntityMetaKey.Name, name)
            .Int(EntityMetaKey.EffectColor, 0)
            .Byte(EntityMetaKey.EffectAmbience, 0)
            .Scale()
            .Dimensions(0.6f, 1.8f)
            .Byte(EntityMetaKey.AlwaysShowNameTag, 1)
            .WriteTo(ref writer);
    }

    /// <summary>Single FLAGS entry for runtime sneak/sprint dirty fan-out (§53).</summary>
    public static void WriteFlagsOnly(ref BinaryStream writer, long flags) =>
        new EntityMetadataBuilder().Flags(flags).WriteTo(ref writer);

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
    public static void WriteFallingBlockMetadata(ref BinaryStream writer, int variant) =>
        new EntityMetadataBuilder()
            .Flags(EntityFlag.Bit(EntityFlag.AffectedByGravity) | EntityFlag.Bit(EntityFlag.HasCollision))
            .Scale()
            .Variant(variant)
            .WriteTo(ref writer);

    /// <summary>Minimal metadata required for a client-rendered zombie actor. Height corrected to the Bedrock-specific 1.9 (was Java's 1.95) — Phase XXIII-B hitbox pass.</summary>
    public static void WriteZombieMetadata(ref BinaryStream writer) =>
        new EntityMetadataBuilder()
            .Flags(BuildSpawnFlags())
            .Scale()
            .Dimensions(0.6f, 1.9f)
            .WriteTo(ref writer);

    /// <summary>
    /// Phase XXIII fix — Bat previously used <see cref="WriteScaleMetadata"/> (Scale only, no FLAGS
    /// entry at all), so the client had no signal to distinguish "flying" from vanilla Bat's other
    /// natural pose: RESTING (bit 23, confirmed against bedrock-protocol's
    /// <c>EntityMetadataFlags::RESTING</c> and gophertunnel's <c>EntityDataFlagResting</c>) — hanging
    /// upside-down attached to a surface. Zenith's Bat always flies in open air, never perches, so
    /// RESTING must be explicitly clear. AffectedByGravity is also intentionally omitted — a flying
    /// mob should not fall. Dimensions (Phase XXIII-B): width 0.5, height 0.9.
    /// </summary>
    public static void WriteBatMetadata(ref BinaryStream writer) =>
        new EntityMetadataBuilder()
            // Resting (bit 23) deliberately left clear.
            .Flags(EntityFlag.Bit(EntityFlag.HasCollision) | EntityFlag.Bit(EntityFlag.ShowName) | EntityFlag.Bit(EntityFlag.AlwaysShowName))
            .Scale()
            .Dimensions(0.5f, 0.9f)
            .WriteTo(ref writer);

    /// <summary>Base non-player mob FLAGS (collision/name), no gravity assumption — ground mobs get gravity from world physics, not this flag, elsewhere in Zenith's movement code.</summary>
    public static long BuildMobFlags() =>
        EntityFlag.Bit(EntityFlag.HasCollision) | EntityFlag.Bit(EntityFlag.ShowName) | EntityFlag.Bit(EntityFlag.AlwaysShowName);

    /// <summary>Minimal metadata for renderable actors without a type-specific data shape.</summary>
    public static void WriteScaleMetadata(ref BinaryStream writer) =>
        new EntityMetadataBuilder().Scale().WriteTo(ref writer);

    /// <summary>
    /// Phase XXIII-B hitbox pass — every ground/passive mob other than Zombie/Bat previously used
    /// <see cref="WriteScaleMetadata"/> (Scale only, no Width/Height at all), leaving the client to
    /// fall back on whatever default dimensions it has, rather than the real Bedrock-specific
    /// per-species hitbox (found via real-client report: "hitbox placeholder"). Dimensions confirmed
    /// against the Minecraft Wiki's entity hitbox table (Bedrock Edition column where it differs
    /// from Java).
    /// </summary>
    public static void WriteMobDimensionMetadata(ref BinaryStream writer, float width, float height) =>
        new EntityMetadataBuilder()
            .Flags(BuildMobFlags())
            .Scale()
            .Dimensions(width, height)
            .WriteTo(ref writer);
}
