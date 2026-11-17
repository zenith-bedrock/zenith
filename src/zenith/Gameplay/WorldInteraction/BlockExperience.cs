using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>The experience a mined block gives (plan 004 item 16): ores that drop an item rather than themselves, vanilla's ranges.</summary>
static class BlockExperience
{
    private static readonly Dictionary<string, (int Min, int Max)> ByBlock = BuildTable();

    private static Dictionary<string, (int Min, int Max)> BuildTable()
    {
        var table = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            ["minecraft:coal_ore"] = (0, 2),
            ["minecraft:deepslate_coal_ore"] = (0, 2),
            ["minecraft:diamond_ore"] = (3, 7),
            ["minecraft:deepslate_diamond_ore"] = (3, 7),
            ["minecraft:emerald_ore"] = (3, 7),
            ["minecraft:deepslate_emerald_ore"] = (3, 7),
            ["minecraft:lapis_ore"] = (2, 5),
            ["minecraft:deepslate_lapis_ore"] = (2, 5),
            ["minecraft:redstone_ore"] = (1, 5),
            ["minecraft:lit_redstone_ore"] = (1, 5),
            ["minecraft:deepslate_redstone_ore"] = (1, 5),
            ["minecraft:lit_deepslate_redstone_ore"] = (1, 5),
            ["minecraft:quartz_ore"] = (2, 5),
            ["minecraft:nether_gold_ore"] = (0, 1),
        };
        return table;
    }

    public static bool TryRoll(int blockRuntimeId, Random random, out int points)
    {
        points = 0;
        if (Blocks.Catalog.States.FamilyOf(blockRuntimeId) is not { } family || !ByBlock.TryGetValue(family.Name, out var range)) return false;
        points = random.Next(range.Min, range.Max + 1);
        return points > 0;
    }
}
