using System.Buffers.Binary;
using Zenith.Player;

namespace Zenith.World;

/// <summary>Main inventory and transient custody share one storage record (ADR §158).</summary>
static class PlayerInventorySave
{
    private const byte EnvelopeVersion = 128;
    private const int FixedUiSlots = 1 + PlayerCraftUi.GridSize + PlayerTableCraftUi.GridSize;

    public static byte[] Pack(Player.Player player)
    {
        var main = player.Inventory.PackMainBlob();
        var outputs = player.Windows.CraftUi.SnapshotOutputs();
        var slots = new InventorySlot[FixedUiSlots + outputs.Length];
        slots[0] = player.Inventory.Cursor;
        for (var i = 0; i < PlayerCraftUi.GridSize; i++) slots[1 + i] = player.Windows.CraftUi.GetGrid(i);
        for (var i = 0; i < PlayerTableCraftUi.GridSize; i++)
            slots[1 + PlayerCraftUi.GridSize + i] = player.Windows.TableCraftUi.GetGrid(i);
        outputs.CopyTo(slots, FixedUiSlots);
        var ui = SlotBlob.Pack(slots);
        var result = new byte[9 + main.Length + ui.Length];
        result[0] = EnvelopeVersion;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(1, 4), main.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(5, 4), outputs.Length);
        main.CopyTo(result, 9); ui.CopyTo(result, 9 + main.Length);
        return result;
    }

    public static bool TryLoad(ReadOnlySpan<byte> data, PlayerInventory inventory, PlayerWindows? windows = null)
    {
        if (data.Length == 0) return false;
        if (data[0] != EnvelopeVersion) return inventory.TryLoadMainFromBlob(data);
        if (data.Length < 9) return false;
        var mainLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(1, 4));
        var outputCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(5, 4));
        if (mainLength < 1 || mainLength > data.Length - 9 || outputCount is < 1 or > PlayerCraftUi.MaxCreatedOutputs)
            return false;
        var slots = new InventorySlot[FixedUiSlots + outputCount];
        if (!SlotBlob.TryUnpack(data[(9 + mainLength)..], slots) ||
            !inventory.TryLoadMainFromBlob(data.Slice(9, mainLength))) return false;
        if (windows is null) return true; // Main-only inspection overload; live login always supplies windows.
        // A legacy oversized cursor remains owned even when new writes obey a smaller capacity.
        inventory.RestoreSnapshot(new InventorySnapshot(inventory.SnapshotMainInventory(), slots[0], inventory.Offhand));
        for (var i = 0; i < PlayerCraftUi.GridSize; i++) windows.CraftUi.TrySetGrid(i, slots[1 + i]);
        for (var i = 0; i < PlayerTableCraftUi.GridSize; i++)
            windows.TableCraftUi.TrySetGrid(i, slots[1 + PlayerCraftUi.GridSize + i]);
        windows.CraftUi.RestoreOutputs(slots[FixedUiSlots..]);
        windows.RecoverUiToInventory(inventory);
        return true;
    }
}
