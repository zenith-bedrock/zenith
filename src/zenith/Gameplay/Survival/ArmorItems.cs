using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// Concrete armor identity + protection table for the first armor slice (Phase XI.2). A data
/// lookup, same shape as <see cref="FoodItems"/> — not an item-component or stats framework.
/// Protection points are vanilla-parity (full diamond/netherite set totals 20, the vanilla cap).
/// </summary>
static class ArmorItems
{
    private static readonly Dictionary<string, (int Slot, float Protection)> Table = new(StringComparer.Ordinal)
    {
        ["minecraft:leather_helmet"] = (PlayerInventory.ArmorHelmetSlot, 1),
        ["minecraft:leather_chestplate"] = (PlayerInventory.ArmorChestplateSlot, 3),
        ["minecraft:leather_leggings"] = (PlayerInventory.ArmorLeggingsSlot, 2),
        ["minecraft:leather_boots"] = (PlayerInventory.ArmorBootsSlot, 1),

        ["minecraft:chainmail_helmet"] = (PlayerInventory.ArmorHelmetSlot, 2),
        ["minecraft:chainmail_chestplate"] = (PlayerInventory.ArmorChestplateSlot, 5),
        ["minecraft:chainmail_leggings"] = (PlayerInventory.ArmorLeggingsSlot, 4),
        ["minecraft:chainmail_boots"] = (PlayerInventory.ArmorBootsSlot, 1),

        ["minecraft:golden_helmet"] = (PlayerInventory.ArmorHelmetSlot, 2),
        ["minecraft:golden_chestplate"] = (PlayerInventory.ArmorChestplateSlot, 5),
        ["minecraft:golden_leggings"] = (PlayerInventory.ArmorLeggingsSlot, 3),
        ["minecraft:golden_boots"] = (PlayerInventory.ArmorBootsSlot, 1),

        ["minecraft:iron_helmet"] = (PlayerInventory.ArmorHelmetSlot, 2),
        ["minecraft:iron_chestplate"] = (PlayerInventory.ArmorChestplateSlot, 6),
        ["minecraft:iron_leggings"] = (PlayerInventory.ArmorLeggingsSlot, 5),
        ["minecraft:iron_boots"] = (PlayerInventory.ArmorBootsSlot, 2),

        ["minecraft:diamond_helmet"] = (PlayerInventory.ArmorHelmetSlot, 3),
        ["minecraft:diamond_chestplate"] = (PlayerInventory.ArmorChestplateSlot, 8),
        ["minecraft:diamond_leggings"] = (PlayerInventory.ArmorLeggingsSlot, 6),
        ["minecraft:diamond_boots"] = (PlayerInventory.ArmorBootsSlot, 3),

        ["minecraft:netherite_helmet"] = (PlayerInventory.ArmorHelmetSlot, 3),
        ["minecraft:netherite_chestplate"] = (PlayerInventory.ArmorChestplateSlot, 8),
        ["minecraft:netherite_leggings"] = (PlayerInventory.ArmorLeggingsSlot, 6),
        ["minecraft:netherite_boots"] = (PlayerInventory.ArmorBootsSlot, 3),
    };

    /// <summary>The armor slot an item belongs in, and its vanilla-parity protection points.</summary>
    public static bool TryGet(ItemPalette palette, StackId id, out int slot, out float protectionPoints)
    {
        slot = -1;
        protectionPoints = 0;
        if (!id.IsItem) return false;
        if (!palette.TryGetName(id.Value, out var name)) return false;
        if (!Table.TryGetValue(name, out var entry)) return false;
        slot = entry.Slot;
        protectionPoints = entry.Protection;
        return true;
    }
}
