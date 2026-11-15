using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>Plain melee item damage; independent of the target species (ADR §159).</summary>
static class PlayerMeleeDamage
{
    public static float Resolve(Player.Player attacker, ItemPalette palette)
    {
        var stack = attacker.Inventory.Get(attacker.SelectedHotbarSlot);
        if (stack.IsEmpty || !stack.Id.IsItem || !palette.TryGetName(stack.Id.Value, out var name)) return 1;
        if (name == "minecraft:trident") return 9;
        var separator = name.LastIndexOf('_');
        if (separator < 0) return 1;
        var tier = name[..separator] switch
        {
            "minecraft:wooden" or "minecraft:golden" => 5,
            "minecraft:stone" or "minecraft:copper" => 6,
            "minecraft:iron" => 7,
            "minecraft:diamond" => 8,
            "minecraft:netherite" => 9,
            _ => 0
        };
        if (tier == 0) return 1;
        return name[(separator + 1)..] switch
        {
            "sword" => tier,
            "axe" => tier - 1,
            "pickaxe" => tier - 2,
            "shovel" => tier - 3,
            _ => 1
        };
    }
}
