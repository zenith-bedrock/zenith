using Zenith.Raknet.Stream;

namespace Zenith.Packets;

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

    /// <summary>
    /// ItemStack (gophertunnel <c>Item</c>) — CreativeContent groups/items.
    /// Not the same as <see cref="Write"/> / ItemInstanceNew.
    /// </summary>
    public void WriteItem(ref BinaryStream writer)
    {
        if (NetworkId == 0)
        {
            writer.WriteVarInt(0);
            return;
        }

        writer.WriteVarInt(NetworkId);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);
        writer.WriteVarInt(BlockRuntimeId);

        // Extra blob: int16 NBT length 0 + empty can_place/can_break uint32 lists (gophertunnel Item).
        Span<byte> extra = stackalloc byte[10];
        extra[0] = 0;
        extra[1] = 0; // int16 LE length
        // uint32 LE can_place count = 0
        extra[2] = 0;
        extra[3] = 0;
        extra[4] = 0;
        extra[5] = 0;
        // uint32 LE can_break count = 0
        extra[6] = 0;
        extra[7] = 0;
        extra[8] = 0;
        extra[9] = 0;
        writer.WriteUnsignedVarInt(extra.Length);
        writer.Write(extra);
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
