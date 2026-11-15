using System.Collections.Frozen;

namespace Zenith.World;

/// <summary>Full cubes supported by the existing place/collision/self-drop cycle (ADR §161).</summary>
static class SolidBuildingBlocks
{
    private static readonly FrozenDictionary<string, DigProfile> Profiles = Build();
    public static IEnumerable<string> Names => Profiles.Keys;

    public static bool TryGet(int runtimeId, out DigProfile profile)
    {
        profile = default;
        return Blocks.TryGetName(runtimeId, out var name) && Profiles.TryGetValue(name, out profile);
    }

    private static FrozenDictionary<string, DigProfile> Build()
    {
        var profiles = new Dictionary<string, DigProfile>(StringComparer.Ordinal);
        string[] woods = ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove",
            "cherry", "bamboo", "crimson", "warped", "pale_oak"];
        foreach (var wood in woods) Add(wood + "_planks", 2, ToolKind.Axe, false);
        string[] colors = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
            "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];
        foreach (var color in colors)
        {
            Add(color + "_wool", 0.8, ToolKind.None, false);
            Add(color + "_concrete", 1.8, ToolKind.Pickaxe, true);
            Add(color + "_terracotta", 1.25, ToolKind.Pickaxe, true);
        }
        Add("hardened_clay", 1.25, ToolKind.Pickaxe, true);
        foreach (var name in new[] { "granite", "polished_granite", "diorite", "polished_diorite",
                     "andesite", "polished_andesite", "stone_bricks", "mossy_stone_bricks",
                     "cracked_stone_bricks", "chiseled_stone_bricks" })
            Add(name, 1.5, ToolKind.Pickaxe, true);
        foreach (var name in new[] { "brick_block", "mossy_cobblestone", "polished_blackstone_bricks",
                     "cracked_polished_blackstone_bricks", "nether_brick", "red_nether_brick" })
            Add(name, 2, ToolKind.Pickaxe, true);
        return profiles.ToFrozenDictionary(StringComparer.Ordinal);

        void Add(string name, double hardness, ToolKind tool, bool required) => profiles.Add("minecraft:" + name,
            new DigProfile(hardness, required ? tool : ToolKind.None, tool, required));
    }
}
