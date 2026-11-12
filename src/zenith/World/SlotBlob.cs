using System.Buffers.Binary;
using Zenith.Player;

namespace Zenith.World;

/// <summary>
/// Packed inventory/chest slot blobs for LevelDB (ADR §39 / §55).
/// v1: version + (i32 value, i32 count) — value assumed Block (migrate-on-read).
/// v2: version + (u8 kind, i32 value, i32 count) per slot.
/// v3 (ADR §147): version + (u8 kind, i32 value, i32 aux, i32 count) per slot; v1/v2 load with aux 0.
/// v4 (ADR §148): version + (u8 kind, i32 value, i32 aux, i32 damage, i32 count) per slot; v1-v3 load with damage 0.
/// </summary>
static class SlotBlob
{
    public const byte Version1 = 1;
    public const byte Version2 = 2;
    public const byte Version3 = 3;
    public const byte Version4 = 4;
    public const byte Version = Version4;
    private const int SlotBytesV3 = 13;
    private const int SlotBytesV4 = 17;

    public static byte[] Pack(ReadOnlySpan<InventorySlot> slots)
    {
        var bytes = new byte[1 + slots.Length * SlotBytesV4];
        bytes[0] = Version4;
        for (var i = 0; i < slots.Length; i++)
        {
            var o = 1 + i * SlotBytesV4;
            var slot = slots[i];
            if (slot.IsEmpty)
            {
                bytes[o] = (byte)StackKind.Block;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 1, 4), Blocks.Air);
                continue; // aux, damage and count stay 0
            }

            bytes[o] = (byte)slot.Id.Kind;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 1, 4), slot.Id.Value);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 5, 4), slot.Id.Aux);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 9, 4), slot.Id.Damage);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 13, 4), slot.Count);
        }

        return bytes;
    }

    public static bool TryUnpack(ReadOnlySpan<byte> data, Span<InventorySlot> destination)
    {
        if (data.Length < 1) return false;
        return data[0] switch
        {
            Version4 => TryUnpackAuxLayout(data, destination, hasDamage: true),
            Version3 => TryUnpackAuxLayout(data, destination, hasDamage: false),
            Version2 => TryUnpackV2(data, destination),
            Version1 => TryUnpackV1(data, destination),
            _ => false
        };
    }

    /// <summary>v3 and v4 differ only by the damage field, so they share one reader.</summary>
    private static bool TryUnpackAuxLayout(ReadOnlySpan<byte> data, Span<InventorySlot> destination, bool hasDamage)
    {
        var slotBytes = hasDamage ? SlotBytesV4 : SlotBytesV3;
        if (data.Length < 1 + destination.Length * slotBytes) return false;

        for (var i = 0; i < destination.Length; i++)
        {
            var o = 1 + i * slotBytes;
            var kindByte = data[o];
            if (kindByte is not ((byte)StackKind.Block or (byte)StackKind.Item))
                return false;
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + 1, 4));
            var aux = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + 5, 4));
            var damage = hasDamage ? BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + 9, 4)) : 0;
            var count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(o + slotBytes - 4, 4));
            var isBlock = (StackKind)kindByte == StackKind.Block;
            if (aux is < 0 or > StackId.MaxAux || damage is < 0 or > ItemUserData.MaxDamage || (isBlock && (aux != 0 || damage != 0)))
                return false;
            var id = new StackId((StackKind)kindByte, value, aux, new ItemUserData(damage));
            destination[i] = count <= 0 || id.IsAirBlock ? InventorySlot.Empty : new InventorySlot(id, count);
        }

        return true;
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
