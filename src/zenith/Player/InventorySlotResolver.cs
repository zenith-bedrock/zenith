using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Resolves a domain <see cref="InventorySlotReference"/> to the live <see cref="InventorySlot"/>
/// it currently holds, and back. Pure domain-state read/write over Player/World - no wire types,
/// no gameplay decisions
/// of its own (callers in Gameplay decide what to do with the slot; Protocol only reads it to
/// build a response). Previously duplicated near-verbatim between
/// Gameplay/Systems/InventorySystem.cs and Protocol/InventoryProtocol.cs.
/// </summary>
static class InventorySlotResolver
{
    public static InventorySlot GetSlot(Player player, World.World world, in InventorySlotReference reference)
    {
        switch (reference.Area)
        {
            case InventorySlotArea.OpenContainer:
                if (player.OpenContainer is not { Target: OpenContainerSession.TargetKind.Chest, Chest: { } view })
                    return InventorySlot.Empty;
                if (reference.Index < 0 || reference.Index >= view.SlotCount) return InventorySlot.Empty;
                return world.Chests.GetOpen(view, reference.Index);

            case InventorySlotArea.CraftGrid:
                return reference.Index is >= 0 and < PlayerCraftUi.GridSize
                    ? player.CraftUi.GetGrid(reference.Index)
                    : InventorySlot.Empty;

            case InventorySlotArea.TableCraftGrid:
                // Only readable while a crafting table is actually the open container (ADR §139) —
                // same "structural decode, authoritative-state gate at resolve time" pattern Chest
                // already uses above for its own Target check.
                return player.IsAtCraftingTable && reference.Index is >= 0 and < PlayerTableCraftUi.GridSize
                    ? player.TableCraftUi.GetGrid(reference.Index)
                    : InventorySlot.Empty;

            case InventorySlotArea.CraftResult:
                return reference.Index == 0 ? player.CraftUi.Result : InventorySlot.Empty;

            case InventorySlotArea.Cursor:
                return reference.Index == 0 ? player.Inventory.Cursor : InventorySlot.Empty;

            case InventorySlotArea.PlayerInventory:
                return PlayerInventory.IsValidInventorySlot(reference.Index)
                    ? player.Inventory.Get(reference.Index)
                    : InventorySlot.Empty;

            case InventorySlotArea.Armor:
                return player.Inventory.GetArmor(reference.Index);

            default:
                return InventorySlot.Empty;
        }
    }

    public static bool TrySetSlot(Player player, World.World world, in InventorySlotReference reference, InventorySlot value)
    {
        switch (reference.Area)
        {
            case InventorySlotArea.OpenContainer:
                if (player.OpenContainer is not { Target: OpenContainerSession.TargetKind.Chest, Chest: { } view })
                    return false;
                return reference.Index >= 0 && reference.Index < view.SlotCount &&
                       world.Chests.TrySetOpen(view, reference.Index, value);

            case InventorySlotArea.CraftGrid:
                return reference.Index >= 0 && reference.Index < PlayerCraftUi.GridSize &&
                       player.CraftUi.TrySetGrid(reference.Index, value);

            case InventorySlotArea.TableCraftGrid:
                return player.IsAtCraftingTable &&
                       reference.Index >= 0 && reference.Index < PlayerTableCraftUi.GridSize &&
                       player.TableCraftUi.TrySetGrid(reference.Index, value);

            case InventorySlotArea.CraftResult:
                return reference.Index == 0 && player.CraftUi.TrySetResult(value);

            case InventorySlotArea.Cursor:
                return reference.Index == 0 && player.Inventory.TrySet(
                    PlayerInventory.CursorSlot,
                    value.IsEmpty ? StackId.FromBlock(Blocks.Air) : value.Id,
                    value.IsEmpty ? 0 : value.Count);

            case InventorySlotArea.PlayerInventory:
                return PlayerInventory.IsValidInventorySlot(reference.Index) && player.Inventory.TrySet(
                    reference.Index,
                    value.IsEmpty ? StackId.FromBlock(Blocks.Air) : value.Id,
                    value.IsEmpty ? 0 : value.Count);

            case InventorySlotArea.Armor:
                return player.Inventory.TrySetArmor(
                    reference.Index,
                    value.IsEmpty ? StackId.FromBlock(Blocks.Air) : value.Id,
                    value.IsEmpty ? 0 : value.Count);

            default:
                return false;
        }
    }
}
