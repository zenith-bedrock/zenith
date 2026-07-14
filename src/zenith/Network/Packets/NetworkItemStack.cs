using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>Network item stack descriptor (InventoryContent / ItemInstanceNew / AddPlayer ItemInstance).</summary>
readonly record struct NetworkItemStack(short NetworkId, ushort Count, int BlockRuntimeId, int Meta = 0, int StackNetworkId = 0)
{
    public static NetworkItemStack Empty => new(0, 0, 0);

    /// <summary>ItemInstanceNew — InventoryContent, MobEquipment (protocol ~1.16+).</summary>
    public void Write(ref BinaryStream writer)
    {
        writer.WriteShort(NetworkId, BinaryStream.Endianess.Little);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);

        var hasNetId = StackNetworkId != 0;
        writer.WriteBool(hasNetId);
        if (hasNetId)
        {
            writer.WriteUnsignedVarInt(0); // stack_id_variant
            writer.WriteVarInt(StackNetworkId);
        }

        writer.WriteUnsignedVarInt(BlockRuntimeId);
        writer.WriteUnsignedVarInt(0); // raw_extra_data length
    }

    /// <summary>ItemInstance legacy — AddPlayer held field (VarInt network id first).</summary>
    public void WriteLegacyItemInstance(ref BinaryStream writer)
    {
        if (NetworkId == 0)
        {
            writer.WriteVarInt(0);
            return;
        }

        writer.WriteVarInt(NetworkId);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);
        writer.WriteBool(false); // no stack net id for peer display
        writer.WriteVarInt(BlockRuntimeId);
        writer.WriteUnsignedVarInt(0); // extra bytes
    }
}
