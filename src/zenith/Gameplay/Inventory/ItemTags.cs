namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Membership of the item tags that crafting recipes reference (a real BDS CraftingData uses exactly
/// these twelve, see tools/bedrock-data/extract_recipes.py). Membership is by item name, so it needs no
/// numeric ids and survives palette refreshes. An unknown tag matches nothing — a recipe that references
/// it simply never crafts, instead of crafting with the wrong ingredients.
/// </summary>
static class ItemTags
{
    private static readonly string[] Woods =
        ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "cherry", "pale_oak", "bamboo", "crimson", "warped"];

    public static bool Contains(string tag, string name)
    {
        if (!name.StartsWith("minecraft:", StringComparison.Ordinal)) return false;
        var n = name.AsSpan("minecraft:".Length);
        return tag switch
        {
            "minecraft:planks" => n.EndsWith("_planks"),
            "minecraft:wooden_slabs" => n.EndsWith("_slab") && IsWoodPrefixed(n, "_slab") && !n.StartsWith("petrified"),
            "minecraft:logs" => IsLog(n),
            "minecraft:logs_that_burn" => IsLog(n) && !n.StartsWith("crimson") && !n.StartsWith("warped"),
            "minecraft:wool" => n.EndsWith("_wool"),
            "minecraft:coals" => n is "coal" or "charcoal",
            "minecraft:egg" => n is "egg" or "blue_egg" or "brown_egg",
            "minecraft:metal_nuggets" => n is "iron_nugget" or "gold_nugget" or "copper_nugget",
            "minecraft:stone_crafting_materials" or "minecraft:stone_tool_materials" =>
                n is "cobblestone" or "blackstone" or "cobbled_deepslate",
            "minecraft:soul_fire_base_blocks" => n is "soul_sand" or "soul_soil",
            "minecraft:mushrooms_for_stew" => n is "brown_mushroom" or "red_mushroom",
            _ => false
        };
    }

    private static bool IsWoodPrefixed(ReadOnlySpan<char> n, string suffix)
    {
        foreach (var wood in Woods)
            if (n.StartsWith(wood) && n.Length == wood.Length + suffix.Length)
                return true;
        return false;
    }

    private static bool IsLog(ReadOnlySpan<char> n)
    {
        if (n.StartsWith("stripped_")) n = n["stripped_".Length..];
        return n.EndsWith("_log") || n.EndsWith("_stem") || n.EndsWith("_wood") || n.EndsWith("_hyphae");
    }
}
