using Zenith.Player;
using Zenith.Raknet.Stream;
using Zenith.World;

namespace Zenith.Network.Packets;

/// <summary>Network item stack descriptor (InventoryContent / held item wire).</summary>
readonly record struct NetworkItemStack(short NetworkId, ushort Count, int BlockRuntimeId, int Meta = 0)
{
    public static NetworkItemStack Empty => new(0, 0, 0);

    public static NetworkItemStack FromBlockSlot(InventorySlot slot)
    {
        if (slot.IsEmpty) return Empty;
        return new NetworkItemStack(
            Items.NetworkIdForBlock(slot.RuntimeId),
            (ushort)slot.Count,
            slot.RuntimeId);
    }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteShort(NetworkId, BinaryStream.Endianess.Little);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);
        writer.WriteBool(false); // hasNetId
        writer.WriteUnsignedVarInt(BlockRuntimeId);
        writer.WriteUnsignedVarInt(0); // raw_extra_data length
    }
}
