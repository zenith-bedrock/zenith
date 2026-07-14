using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>Network item stack descriptor (InventoryContent wire).</summary>
readonly record struct NetworkItemStack(short NetworkId, ushort Count, int BlockRuntimeId, int Meta = 0)
{
    public static NetworkItemStack Empty => new(0, 0, 0);

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
