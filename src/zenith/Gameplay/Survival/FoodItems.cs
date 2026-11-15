using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// Concrete food facts used by the hunger tick; item-specific effects remain separate.
/// </summary>
readonly record struct FoodDefinition(int Nutrition, float Saturation, int UseTicks = 32,
    bool AlwaysEdible = false, string? Remainder = null);

static class FoodItems
{
    // Food/saturation facts cross-checked against PocketMine item definitions. Effects such as
    // poison/absorption and chorus teleport are separate gameplay behavior, not modeled here.
    private static readonly Dictionary<string, FoodDefinition> Foods = new(StringComparer.Ordinal)
    {
        ["minecraft:apple"] = new(4, 2.4f),
        ["minecraft:bread"] = new(5, 6),
        ["minecraft:carrot"] = new(3, 4.8f),
        ["minecraft:potato"] = new(1, 0.6f),
        ["minecraft:baked_potato"] = new(5, 7.2f),
        ["minecraft:melon_slice"] = new(2, 1.2f),
        ["minecraft:cooked_beef"] = new(8, 12.8f),
        ["minecraft:cooked_porkchop"] = new(8, 12.8f),
        ["minecraft:cooked_chicken"] = new(6, 7.2f),
        ["minecraft:cooked_cod"] = new(5, 6),
        ["minecraft:cooked_salmon"] = new(6, 9.6f),
        ["minecraft:cooked_mutton"] = new(6, 9.6f),
        ["minecraft:cooked_rabbit"] = new(5, 6),
        ["minecraft:beef"] = new(3, 1.8f),
        ["minecraft:porkchop"] = new(3, 0.6f),
        ["minecraft:chicken"] = new(2, 1.2f),
        ["minecraft:mutton"] = new(2, 1.2f),
        ["minecraft:rabbit"] = new(3, 1.8f),
        ["minecraft:cod"] = new(2, 0.4f),
        ["minecraft:salmon"] = new(2, 0.2f),
        ["minecraft:tropical_fish"] = new(1, 0.2f),
        ["minecraft:pufferfish"] = new(1, 0.2f),
        ["minecraft:beetroot"] = new(1, 1.2f),
        ["minecraft:cookie"] = new(2, 0.4f),
        ["minecraft:pumpkin_pie"] = new(8, 4.8f),
        ["minecraft:dried_kelp"] = new(1, 0.6f, UseTicks: 16),
        ["minecraft:sweet_berries"] = new(2, 1.2f),
        ["minecraft:glow_berries"] = new(2, 0.4f),
        ["minecraft:golden_carrot"] = new(6, 14.4f),
        ["minecraft:golden_apple"] = new(4, 9.6f, AlwaysEdible: true),
        ["minecraft:enchanted_golden_apple"] = new(4, 9.6f, AlwaysEdible: true),
        ["minecraft:chorus_fruit"] = new(4, 2.4f, AlwaysEdible: true),
        ["minecraft:rotten_flesh"] = new(4, 0.8f),
        ["minecraft:spider_eye"] = new(2, 3.2f),
        ["minecraft:poisonous_potato"] = new(2, 1.2f),
        ["minecraft:mushroom_stew"] = new(6, 7.2f, Remainder: "minecraft:bowl"),
        ["minecraft:beetroot_soup"] = new(6, 7.2f, Remainder: "minecraft:bowl"),
        ["minecraft:rabbit_stew"] = new(10, 12, Remainder: "minecraft:bowl"),
        ["minecraft:suspicious_stew"] = new(6, 7.2f, Remainder: "minecraft:bowl"),
        ["minecraft:honey_bottle"] = new(6, 1.2f, UseTicks: 40, AlwaysEdible: true, Remainder: "minecraft:glass_bottle"),
    };

    public static bool TryGetNutrition(ItemPalette palette, StackId id, out int nutrition)
    {
        var found = TryGet(palette, id, out var food);
        nutrition = food.Nutrition;
        return found;
    }

    public static bool TryGet(ItemPalette palette, StackId id, out FoodDefinition food)
    {
        food = default;
        return id.IsItem && palette.TryGetName(id.Value, out var name) && Foods.TryGetValue(name, out food);
    }
}
