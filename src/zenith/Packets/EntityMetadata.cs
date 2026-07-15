using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Keys / types / flags do entity metadata Bedrock (wire).</summary>
static class EntityMetaKey
{
    public const int Flags = 0;
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
    /// <summary>FLAGS(+Breathing) + visible-name entries used by peer AddPlayer and local SetActorData.</summary>
    public static void WriteVisibleNameMetadata(ref BinaryStream writer, string name)
    {
        long flags =
            EntityFlag.Bit(EntityFlag.Breathing) |
            EntityFlag.Bit(EntityFlag.CanClimb) |
            EntityFlag.Bit(EntityFlag.HasCollision) |
            EntityFlag.Bit(EntityFlag.AffectedByGravity) |
            EntityFlag.Bit(EntityFlag.ShowName) |
            EntityFlag.Bit(EntityFlag.AlwaysShowName);

        writer.WriteUnsignedVarInt(8);

        writer.WriteUnsignedVarInt(EntityMetaKey.Flags);
        writer.WriteUnsignedVarInt(EntityMetaType.Long);
        writer.WriteVarLong(flags);

        writer.WriteUnsignedVarInt(EntityMetaKey.ColorIndex);
        writer.WriteUnsignedVarInt(EntityMetaType.Byte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.Name);
        writer.WriteUnsignedVarInt(EntityMetaType.String);
        writer.WriteVarString(name);

        writer.WriteUnsignedVarInt(EntityMetaKey.EffectColor);
        writer.WriteUnsignedVarInt(EntityMetaType.Int);
        writer.WriteVarInt(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.EffectAmbience);
        writer.WriteUnsignedVarInt(EntityMetaType.Byte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.Width);
        writer.WriteUnsignedVarInt(EntityMetaType.Float);
        writer.WriteFloat(0.6f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(EntityMetaKey.Height);
        writer.WriteUnsignedVarInt(EntityMetaType.Float);
        writer.WriteFloat(1.8f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(EntityMetaKey.AlwaysShowNameTag);
        writer.WriteUnsignedVarInt(EntityMetaType.Byte);
        writer.WriteByte(1);
    }
}
