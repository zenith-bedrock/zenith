using System.Buffers.Binary;
using Zenith.Player;
using Zenith.World;

namespace Zenith.World;

/// <summary>Packed inventory/chest slot blobs for LevelDB (ADR §39). version=1 + i32 pairs LE.</summary>
static class SlotBlob
{
    public const byte Version = 1;

    public static byte[] Pack(ReadOnlySpan<InventorySlot> slots)
    {
        var bytes = new byte[1 + slots.Length * 8];
        bytes[0] = Version;
        for (var i = 0; i < slots.Length; i++)
        {
            var o = 1 + i * 8;
            var slot = slots[i];
            var rid = slot.IsEmpty ? Blocks.Air : slot.RuntimeId;
            var count = slot.IsEmpty ? 0 : slot.Count;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o, 4), rid);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 4, 4), count);
        }

        return bytes;
    }

    public static bool TryUnpack(ReadOnlySpan<byte> data, Span<InventorySlot> destination)
    {
        var need = 1 + destination.Length * 8;
        if (data.Length < need || data[0] != Version)
            return false;

        for (var i = 0; i < destination.Length; i++)
        {
            var o = 1 + i * 8;
            var rid = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o, 4));
            var count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + 4, 4));
            destination[i] = count <= 0 || rid == Blocks.Air
                ? InventorySlot.Empty
                : new InventorySlot(rid, count);
        }

        return true;
    }
}
