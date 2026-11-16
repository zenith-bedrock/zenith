namespace Zenith.Gameplay.Inventory;

/// <summary>
/// The item names that code outside a data table needs to refer to. A name used in one place still lives here, so a typo
/// cannot hide in a system: <c>ItemNamesTests</c> checks every constant (and every name in the food, armour and tool
/// tables) against the item palette.
/// </summary>
static class ItemNames
{
    public const string Namespace = "minecraft:";

    public const string Air = "minecraft:air";
    public const string Wheat = "minecraft:wheat";
    public const string Emerald = "minecraft:emerald";
    public const string Trident = "minecraft:trident";

    public const string Bucket = "minecraft:bucket";
    public const string WaterBucket = "minecraft:water_bucket";
    public const string LavaBucket = "minecraft:lava_bucket";
    public const string PowderSnowBucket = "minecraft:powder_snow_bucket";
    public const string MilkBucket = "minecraft:milk_bucket";

    /// <summary>The name without its namespace, or the name unchanged when it has none.</summary>
    public static ReadOnlySpan<char> Bare(string name) =>
        name.StartsWith(Namespace, StringComparison.Ordinal) ? name.AsSpan(Namespace.Length) : name;
}
