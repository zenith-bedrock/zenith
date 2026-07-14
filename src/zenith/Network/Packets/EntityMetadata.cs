namespace Zenith.Network.Packets;

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
