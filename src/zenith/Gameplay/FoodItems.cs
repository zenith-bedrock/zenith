using Zenith.World;

namespace Zenith.Gameplay;

/// <summary>
/// Concrete nutrition table for the first food/hunger slice (Phase XI.1). A data lookup, not an
/// item-component framework — extend with more names only when a real recipe/loot path needs them.
/// </summary>
static class FoodItems
{
    private static readonly Dictionary<string, int> Nutrition = new(StringComparer.Ordinal)
    {
        ["minecraft:apple"] = 4,
        ["minecraft:bread"] = 5,
        ["minecraft:carrot"] = 3,
        ["minecraft:potato"] = 1,
        ["minecraft:baked_potato"] = 5,
        ["minecraft:melon_slice"] = 2,
        ["minecraft:cooked_beef"] = 8,
        ["minecraft:cooked_porkchop"] = 8,
        ["minecraft:cooked_chicken"] = 6,
        ["minecraft:cooked_cod"] = 5,
        ["minecraft:cooked_salmon"] = 6,
    };

    public static bool TryGetNutrition(ItemPalette palette, StackId id, out int nutrition)
    {
        nutrition = 0;
        if (!id.IsItem) return false;
        if (!palette.TryGetName(id.Value, out var name)) return false;
        return Nutrition.TryGetValue(name, out nutrition);
    }
}
