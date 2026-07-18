using Zenith.Player;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Wire map inventário: container IDs Bedrock → flat 0–35 / cursor / chest 100+.
/// Sem lógica de gameplay — só boundary (SSOT; ADR §54 Phase 5).
/// </summary>
static class InventoryContainerMap
{
    public const byte CombinedHotbarAndInventory = 12;
    /// <summary>Bedrock crafting input (2×2 / 3×3 UI) — domain flats CraftUiBase+.</summary>
    public const byte CraftingInput = 13;
    /// <summary>Bedrock crafting output preview.</summary>
    public const byte CraftingOutputPreview = 14;
    public const byte Hotbar = 28;
    public const byte Inventory = 29;
    public const byte Cursor = 59;
    /// <summary>Bedrock created-output (craft result pickup).</summary>
    public const byte CreatedOutput = 60;
    public const byte Chest = 7;

    /// <summary>Flat domínio para slots do baú aberto (não vive em <see cref="PlayerInventory"/>).</summary>
    public const int ChestBase = 100;

    /// <summary>Flat domínio craft UI 2×2 (ephemeral, ADR §35).</summary>
    public const int CraftUiBase = 150;

    /// <summary>Created output slot (container 60 wire slot 50).</summary>
    public const int CraftResultFlat = CraftUiBase + PlayerCraftUi.GridSize;

    public const int CraftingGridWireOffset = 28;
    public const byte CraftingResultWireSlot = 50;
    public const int UiInventorySlotCount = 54;

    /// <summary>Mojang PlayerUISlot::CURSOR — window 124 slot 0 (ISR container 59).</summary>
    public const int UiCursorSlot = 0;

    public static bool IsChestFlat(int flat) =>
        flat is >= ChestBase and < ChestBase + ChestStore.Size;

    public static bool IsCraftUiFlat(int flat) =>
        flat is >= CraftUiBase and <= CraftResultFlat;

    public static bool IsCraftGridFlat(int flat) =>
        flat is >= CraftUiBase and < CraftResultFlat;

    public static byte CraftGridWireSlot(int gridIndex) =>
        (byte)(CraftingGridWireOffset + gridIndex);

    public static bool TryMap(byte containerId, byte slot, out int flat)
    {
        switch (containerId)
        {
            case Hotbar:
                if (slot >= PlayerInventory.HotbarSize)
                {
                    flat = 0;
                    return false;
                }

                flat = slot;
                return true;

            case CombinedHotbarAndInventory:
            case Inventory:
                // Containers 12 and 29: slot is absolute bag index 0–35 (not “first inventory = 0”).
                if (slot >= PlayerInventory.FullInventorySize)
                {
                    flat = 0;
                    return false;
                }

                flat = slot;
                return true;

            case Cursor:
                flat = PlayerInventory.CursorSlot;
                return true;

            case Chest:
                if (slot >= ChestStore.Size)
                {
                    flat = 0;
                    return false;
                }

                flat = ChestBase + slot;
                return true;

            case CraftingInput:
                if (slot is >= CraftingGridWireOffset and < CraftingGridWireOffset + PlayerCraftUi.GridSize)
                {
                    flat = CraftUiBase + (slot - CraftingGridWireOffset);
                    return true;
                }

                flat = 0;
                return false;

            case CraftingOutputPreview:
            case CreatedOutput:
                if (slot == CraftingResultWireSlot)
                {
                    flat = CraftResultFlat;
                    return true;
                }

                flat = 0;
                return false;

            default:
                flat = 0;
                return false;
        }
    }

    public static bool TryToWire(int flat, out byte containerId, out byte wireSlot)
    {
        if (flat == PlayerInventory.CursorSlot)
        {
            containerId = Cursor;
            wireSlot = 0;
            return true;
        }

        if (IsChestFlat(flat))
        {
            containerId = Chest;
            wireSlot = (byte)(flat - ChestBase);
            return true;
        }

        if (IsCraftGridFlat(flat))
        {
            containerId = CraftingInput;
            wireSlot = CraftGridWireSlot(flat - CraftUiBase);
            return true;
        }

        if (flat == CraftResultFlat)
        {
            containerId = CreatedOutput;
            wireSlot = CraftingResultWireSlot;
            return true;
        }

        if (flat is >= 0 and < PlayerInventory.HotbarSize)
        {
            containerId = Hotbar;
            wireSlot = (byte)flat;
            return true;
        }

        if (PlayerInventory.IsValidInventorySlot(flat))
        {
            // Container 29 slot = absolute bag index (0–35), not hotbar-relative 0–8.
            containerId = Inventory;
            wireSlot = (byte)flat;
            return true;
        }

        containerId = 0;
        wireSlot = 0;
        return false;
    }
}
