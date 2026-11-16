using Zenith.Gameplay.Inventory;
using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>Plain melee item damage; independent of the target species (ADR §159).</summary>
static class PlayerMeleeDamage
{
    public static float Resolve(Player.Player attacker, ItemPalette palette)
    {
        var stack = attacker.Inventory.Get(attacker.SelectedHotbarSlot);
        if (stack.IsEmpty || !stack.Id.IsItem || !palette.TryGetName(stack.Id.Value, out var name)) return 1;
        if (name == ItemNames.Trident) return 9;
        var bare = ItemNames.Bare(name);
        var separator = bare.LastIndexOf('_');
        if (separator < 0) return 1;
        var tier = bare[..separator] switch
        {
            "wooden" or "golden" => 5,
            "stone" or "copper" => 6,
            "iron" => 7,
            "diamond" => 8,
            "netherite" => 9,
            _ => 0
        };
        if (tier == 0) return 1;
        return bare[(separator + 1)..] switch
        {
            "sword" => tier,
            "axe" => tier - 1,
            "pickaxe" => tier - 2,
            "shovel" => tier - 3,
            _ => 1
        };
    }
}
