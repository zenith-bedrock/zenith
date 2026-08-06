using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Resolves a flat inventory index (see <see cref="InventoryContainerMap"/> for the wire-index
/// boundary that produces these) to the live <see cref="InventorySlot"/> it currently holds, and
/// back. Pure domain-state read/write over Player/World - no wire types, no gameplay decisions
/// of its own (callers in Gameplay decide what to do with the slot; Protocol only reads it to
/// build a response). Previously duplicated near-verbatim between
/// Gameplay/Systems/InventorySystem.cs and Protocol/InventoryProtocol.cs.
/// </summary>
static class InventorySlotResolver
{
    public static InventorySlot GetSlot(Player player, World.World world, int flat)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
        {
            if (player.OpenChest is not { } view) return InventorySlot.Empty;
            var openSlot = flat - InventoryContainerMap.ChestBase;
            if (openSlot < 0 || openSlot >= view.SlotCount) return InventorySlot.Empty;
            return world.Chests.GetOpen(view, openSlot);
        }

        if (InventoryContainerMap.IsCraftGridFlat(flat))
            return player.CraftUi.GetGrid(flat - InventoryContainerMap.CraftUiBase);

        if (flat == InventoryContainerMap.CraftResultFlat)
            return player.CraftUi.Result;

        return player.Inventory.Get(flat);
    }

    public static bool TrySetSlot(Player player, World.World world, int flat, InventorySlot value)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
        {
            if (player.OpenChest is not { } view) return false;
            var openSlot = flat - InventoryContainerMap.ChestBase;
            if (openSlot < 0 || openSlot >= view.SlotCount) return false;
            return world.Chests.TrySetOpen(view, openSlot, value);
        }

        if (InventoryContainerMap.IsCraftGridFlat(flat))
            return player.CraftUi.TrySetGrid(flat - InventoryContainerMap.CraftUiBase, value);

        if (flat == InventoryContainerMap.CraftResultFlat)
            return player.CraftUi.TrySetResult(value);

        return player.Inventory.TrySet(flat, value.IsEmpty ? StackId.FromBlock(Blocks.Air) : value.Id, value.IsEmpty ? 0 : value.Count);
    }
}
