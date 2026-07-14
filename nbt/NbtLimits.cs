namespace Zenith.Nbt;

/// <summary>Hard limits for hostile or corrupt NBT (disk or future network).</summary>
public static class NbtLimits
{
    public const int MaxDepth = 64;
    public const int MaxStringLength = 1_048_576;
    public const int MaxArrayLength = 16_777_216;
    public const int MaxCompoundEntries = 1_048_576;
}
