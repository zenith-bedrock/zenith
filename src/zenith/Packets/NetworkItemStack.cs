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
    /// ItemV4 (protocol 2168+, ADR §87) — i16 LE network id + count + meta + optional stack net
    /// id + block runtime id + extra blob. Use from: InventoryContent, MobEquipment.
    /// </summary>
    public void WriteNetworkItemStackDescriptor(ref BinaryStream writer)
    {
        writer.WriteShort(NetworkId, BinaryStream.Endianess.Little);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);

        // has_stack_id(bool) then a bare stack_id(zigzag32) if present — no separate variant/tag
        // byte (that was the bug: ItemV4 dropped it, unlike the older ItemInstance shape).
        var hasNetId = StackNetworkId != 0;
        writer.WriteBool(hasNetId);
        if (hasNetId)
            writer.WriteVarInt(StackNetworkId);

        writer.WriteUnsignedVarInt(BlockRuntimeId);
        writer.WriteUnsignedVarInt(0); // raw_extra_data length
    }

    /// <summary>
    /// cerealizer&lt;NetworkItemStackDescriptor&gt;::SerializedData — protocol 2168+ shape for
    /// AddPlayer's held item / AddItemActor's item (ADR §82). Fixed i16 network id (not VarInt);
    /// no air-item early-out — every field is always written, matching a cerealised struct's
    /// flat unconditional layout. Net id variant lost its tag byte at 2168 (sign/parity encode
    /// the case instead) — Zenith only ever sends a server-assigned non-negative id outbound, so
    /// that nuance doesn't affect what gets written here.
    /// </summary>
    public void WriteSerializedNetworkItemStackDescriptor(ref BinaryStream writer)
    {
        writer.WriteShort(NetworkId, BinaryStream.Endianess.Little);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);

        var hasNetId = StackNetworkId != 0;
        writer.WriteBool(hasNetId);
        if (hasNetId)
            writer.WriteVarInt(StackNetworkId);

        writer.WriteUnsignedVarInt(BlockRuntimeId);
        writer.WriteUnsignedVarInt(0); // user_data_buffer — empty (no NBT / can-place / can-break tracked)
    }

    /// <summary>
    /// NetworkItemInstanceDescriptor (VarInt network id + block runtime + a conditional extra
    /// blob). Used by CreativeContent (group icons, item entries) and CraftingData outputs.
    ///
    /// The extra blob is NOT unconditional — verified against gophertunnel's <c>Writer.Item</c>/
    /// <c>itemUserData</c> (protocol 2168 tag, `D:\Development\bedrock\gophertunnel`): when
    /// <c>NetworkId == 0</c> (air — e.g. `CreativeContentBuilder`'s Construction group icon,
    /// sent on every join), only a bare zero-length varint is written, NOT the 10-byte
    /// NBT-length + can_place/can_break blob below. Writing the full 10 bytes unconditionally
    /// (the previous shape here) corrupted every byte after it in the packet for that real,
    /// production call site — found by reading the reference implementation, not by symptom.
    /// </summary>
    public void WriteItemStack(ref BinaryStream writer)
    {
        writer.WriteVarInt(NetworkId);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);
        writer.WriteVarInt(BlockRuntimeId);

        if (NetworkId == 0)
        {
            writer.WriteUnsignedVarInt(0); // air: bare empty-user-data marker, no NBT/list blob at all
            return;
        }

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
