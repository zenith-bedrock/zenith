using Zenith.Gameplay.Inventory;
using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>Plain melee item damage; independent of the target species (ADR §159).</summary>
static class PlayerMeleeDamage
{
    /// <summary>A hit struck while falling (not on the ground, not in water, not riding) is critical and does half again as much.</summary>
    public const float CriticalMultiplier = 1.5f;

    public static bool IsCritical(Player.Player attacker) =>
        !attacker.IsOnGround && attacker.FallPeakY > attacker.PositionY && attacker.RidingEntityId is null &&
        SubmersionProbe.AtFeet(attacker) == FluidType.None;

    public static float Resolve(Player.Player attacker, ItemPalette palette) =>
        Base(attacker, palette) * (IsCritical(attacker) ? CriticalMultiplier : 1f);

    private static float Base(Player.Player attacker, ItemPalette palette)
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
