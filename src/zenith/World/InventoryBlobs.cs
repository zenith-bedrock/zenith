using Zenith.Player;

namespace Zenith.World;

/// <summary>
/// The storage encoding of a <see cref="PlayerInventory"/>'s slot groups (<see cref="SlotBlob"/>). The inventory knows nothing of
/// its serialized form: it hands out and takes back slots, and this is the one place that turns them into bytes and back.
/// </summary>
static class InventoryBlobs
{
    public static byte[] PackMainBlob(this PlayerInventory inventory) => SlotBlob.Pack(inventory.SnapshotMainInventory());

    public static bool TryLoadMainFromBlob(this PlayerInventory inventory, ReadOnlySpan<byte> data)
    {
        Span<InventorySlot> slots = new InventorySlot[PlayerInventory.FullInventorySize];
        if (!SlotBlob.TryUnpack(data, slots)) return false;
        inventory.LoadMain(slots);
        return true;
    }

    public static byte[] PackArmorBlob(this PlayerInventory inventory) => SlotBlob.Pack(inventory.SnapshotArmor());

    public static bool TryLoadArmorFromBlob(this PlayerInventory inventory, ReadOnlySpan<byte> data)
    {
        Span<InventorySlot> slots = new InventorySlot[PlayerInventory.ArmorSize];
        if (!SlotBlob.TryUnpack(data, slots)) return false;
        inventory.LoadArmor(slots);
        return true;
    }

    public static byte[] PackOffhandBlob(this PlayerInventory inventory) => SlotBlob.Pack([inventory.Offhand]);

    public static bool TryLoadOffhandFromBlob(this PlayerInventory inventory, ReadOnlySpan<byte> data)
    {
        Span<InventorySlot> slots = new InventorySlot[1];
        if (!SlotBlob.TryUnpack(data, slots)) return false;
        inventory.LoadOffhand(slots[0]);
        return true;
    }
}
