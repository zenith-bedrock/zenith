using System.Buffers.Binary;
using Zenith.Player;

namespace Zenith.World;

/// <summary>
/// Packed inventory/chest slot blobs for LevelDB (ADR §39 / §55).
/// v1: version + (i32 value, i32 count) — value assumed Block (migrate-on-read).
/// v2: version + (u8 kind, i32 value, i32 count) per slot.
/// </summary>
static class SlotBlob
{
    public const byte Version1 = 1;
    public const byte Version2 = 2;
    public const byte Version = Version2;

    public static byte[] Pack(ReadOnlySpan<InventorySlot> slots)
    {
        var bytes = new byte[1 + slots.Length * 9];
        bytes[0] = Version2;
        for (var i = 0; i < slots.Length; i++)
        {
            var o = 1 + i * 9;
            var slot = slots[i];
            if (slot.IsEmpty)
            {
                bytes[o] = (byte)StackKind.Block;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 1, 4), Blocks.Air);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 5, 4), 0);
                continue;
            }

            bytes[o] = (byte)slot.Id.Kind;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 1, 4), slot.Id.Value);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 5, 4), slot.Count);
        }

        return bytes;
    }

    public static bool TryUnpack(ReadOnlySpan<byte> data, Span<InventorySlot> destination)
    {
        if (data.Length < 1) return false;
        return data[0] switch
        {
            Version2 => TryUnpackV2(data, destination),
            Version1 => TryUnpackV1(data, destination),
            _ => false
        };
    }

    private static bool TryUnpackV2(ReadOnlySpan<byte> data, Span<InventorySlot> destination)
    {
        var need = 1 + destination.Length * 9;
        if (data.Length < need) return false;

        for (var i = 0; i < destination.Length; i++)
        {
            var o = 1 + i * 9;
            var kindByte = data[o];
            if (kindByte is not ((byte)StackKind.Block or (byte)StackKind.Item))
                return false;
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + 1, 4));
            var count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + 5, 4));
            var id = new StackId((StackKind)kindByte, value);
            destination[i] = count <= 0 || id.IsAirBlock || (id.IsBlock && id.Value == Blocks.Air)
                ? InventorySlot.Empty
                : new InventorySlot(id, count);
        }

        return true;
    }

    /// <summary>v1 migrate-on-read: tool network ids → Item; else Block (ADR §55).</summary>
    private static bool TryUnpackV1(ReadOnlySpan<byte> data, Span<InventorySlot> destination)
    {
        var need = 1 + destination.Length * 8;
        if (data.Length < need) return false;

        Tools.EnsureLoaded();
        for (var i = 0; i < destination.Length; i++)
        {
            var o = 1 + i * 8;
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o, 4));
            var count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + 4, 4));
            if (count <= 0 || value == Blocks.Air || value == 0)
            {
                destination[i] = InventorySlot.Empty;
                continue;
            }

            var id = Tools.IsTool(value)
                ? StackId.FromItem(value)
                : StackId.FromBlock(value);
            destination[i] = new InventorySlot(id, count);
        }

        return true;
    }
}
