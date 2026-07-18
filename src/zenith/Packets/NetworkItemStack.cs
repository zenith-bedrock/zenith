using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Wire item stack DTO (domain maps via Protocol). One type, three encode shapes —
/// packet <c>Encode</c> picks the writer; Gameplay never calls these.
/// </summary>
readonly record struct NetworkItemStack(short NetworkId, ushort Count, int BlockRuntimeId, int Meta = 0, int StackNetworkId = 0)
{
    public static NetworkItemStack Empty => new(0, 0, 0);

    /// <summary>
    /// NetworkItemStackDescriptor — i16 LE network id + count + meta + optional stack net id.
    /// Use from: InventoryContent, MobEquipment.
    /// </summary>
    public void WriteNetworkItemStackDescriptor(ref BinaryStream writer)
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
    /// ItemStackWrapper / legacy ItemInstance (VarInt network id). Stack net id always omitted.
    /// Use from: AddPlayer held, AddItemActor.
    /// </summary>
    public void WriteItemStackWrapper(ref BinaryStream writer)
    {
        if (NetworkId == 0)
        {
            writer.WriteVarInt(0);
            return;
        }

        writer.WriteVarInt(NetworkId);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);
        writer.WriteBool(false); // no stack net id for entity/held display
        writer.WriteVarInt(BlockRuntimeId);
        writer.WriteUnsignedVarInt(0); // extra bytes
    }

    /// <summary>
    /// ItemStack without stack net id (VarInt network id + block runtime + empty extra blob).
    /// Use from: CreativeContent, CraftingData outputs.
    /// </summary>
    public void WriteItemStack(ref BinaryStream writer)
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

        // Extra blob: int16 NBT length 0 + empty can_place / can_break uint32 lists.
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
}
