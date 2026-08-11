using Zenith.Player;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Wire map inventário: container IDs Bedrock → domain slot references.
/// Sem lógica de gameplay — só boundary (SSOT; ADR §54 Phase 5).
/// </summary>
static class InventoryContainerMap
{
    public const byte CombinedHotbarAndInventory = 12;
    /// <summary>Bedrock crafting input (2×2 / 3×3 UI).</summary>
    public const byte CraftingInput = 13;
    /// <summary>Bedrock crafting output preview.</summary>
    public const byte CraftingOutputPreview = 14;
    public const byte Hotbar = 28;
    public const byte Inventory = 29;
    public const byte Cursor = 59;
    /// <summary>Bedrock created-output (craft result pickup).</summary>
    public const byte CreatedOutput = 60;
    public const byte Chest = 7;

    /// <summary>Wire window ids — Gameplay must not reference <c>InventoryContentPacket</c> (ADR §56 hygiene).</summary>
    public const int WindowInventory = 0;
    public const int WindowChest = 2;
    public const int WindowUI = 124;
    public const byte WindowTypeChest = 0;
    public const byte WindowTypeInventory = 0xff;

    /// <summary>Legacy characterization-test offset for open-container slots; not used by production transactions.</summary>
    public const int ChestBase = 100;

    /// <summary>
    /// Legacy characterization-test offset for the ephemeral craft UI (ADR §35).
    /// </summary>
    public const int CraftUiBase = 200;

    /// <summary>Legacy characterization-test offset for created output (container 60 wire slot 50).</summary>
    public const int CraftResultFlat = CraftUiBase + PlayerCraftUi.GridSize;

    public const int CraftingGridWireOffset = 28;
    public const byte CraftingResultWireSlot = 50;
    public const int UiInventorySlotCount = 54;

    /// <summary>Mojang PlayerUISlot::CURSOR — window 124 slot 0 (ISR container 59).</summary>
    public const int UiCursorSlot = 0;

    public static bool IsChestFlat(int flat) =>
        flat is >= ChestBase and < ChestBase + ChestStore.DoubleSize;

    public static bool IsCraftUiFlat(int flat) =>
        flat is >= CraftUiBase and <= CraftResultFlat;

    public static bool IsCraftGridFlat(int flat) =>
        flat is >= CraftUiBase and < CraftResultFlat;

    public static byte CraftGridWireSlot(int gridIndex) =>
        (byte)(CraftingGridWireOffset + gridIndex);

    public static bool TryMap(byte containerId, byte slot, out InventorySlotReference reference)
    {
        switch (containerId)
        {
            case Hotbar:
                if (slot >= PlayerInventory.HotbarSize)
                {
                    reference = default;
                    return false;
                }

                reference = InventorySlotReference.Player(slot);
                return true;

            case CombinedHotbarAndInventory:
            case Inventory:
                // Containers 12 and 29: slot is absolute bag index 0–35 (not “first inventory = 0”).
                if (slot >= PlayerInventory.FullInventorySize)
                {
                    reference = default;
                    return false;
                }

                reference = InventorySlotReference.Player(slot);
                return true;

            case Cursor:
                reference = InventorySlotReference.Cursor;
                return true;

            case Chest:
                if (slot >= ChestStore.DoubleSize)
                {
                    reference = default;
                    return false;
                }

                reference = InventorySlotReference.OpenContainer(slot);
                return true;

            case CraftingInput:
                if (slot is >= CraftingGridWireOffset and < CraftingGridWireOffset + PlayerCraftUi.GridSize)
                {
                    reference = InventorySlotReference.CraftGrid(slot - CraftingGridWireOffset);
                    return true;
                }

                reference = default;
                return false;

            case CraftingOutputPreview:
            case CreatedOutput:
                if (slot == CraftingResultWireSlot)
                {
                    reference = InventorySlotReference.CraftResult;
                    return true;
                }

                reference = default;
                return false;

            default:
                reference = default;
                return false;
        }
    }

    public static bool TryToWire(in InventorySlotReference reference, out byte containerId, out byte wireSlot)
    {
        switch (reference.Area)
        {
            case InventorySlotArea.Cursor when reference.Index == 0:
                containerId = Cursor;
                wireSlot = 0;
                return true;
            case InventorySlotArea.OpenContainer when reference.Index is >= 0 and < ChestStore.DoubleSize:
                containerId = Chest;
                wireSlot = (byte)reference.Index;
                return true;
            case InventorySlotArea.CraftGrid when reference.Index is >= 0 and < PlayerCraftUi.GridSize:
                containerId = CraftingInput;
                wireSlot = CraftGridWireSlot(reference.Index);
                return true;
            case InventorySlotArea.CraftResult when reference.Index == 0:
                containerId = CreatedOutput;
                wireSlot = CraftingResultWireSlot;
                return true;
            case InventorySlotArea.PlayerInventory when reference.Index is >= 0 and < PlayerInventory.HotbarSize:
                containerId = Hotbar;
                wireSlot = (byte)reference.Index;
                return true;
            case InventorySlotArea.PlayerInventory when PlayerInventory.IsValidInventorySlot(reference.Index):
                // Container 29 slot = absolute bag index (0–35), not hotbar-relative 0–8.
                containerId = Inventory;
                wireSlot = (byte)reference.Index;
                return true;
            default:
                containerId = 0;
                wireSlot = 0;
                return false;
        }
    }

    /// <summary>Compatibility only for flat-index characterization tests.</summary>
    public static bool TryToWire(int flat, out byte containerId, out byte wireSlot)
    {
        if (!InventorySlotReference.TryFromLegacyFlat(flat, out var reference))
        {
            containerId = 0;
            wireSlot = 0;
            return false;
        }

        return TryToWire(reference, out containerId, out wireSlot);
    }
}
