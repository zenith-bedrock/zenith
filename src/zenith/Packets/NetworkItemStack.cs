using Zenith.Binary;
using Zenith.Nbt;

namespace Zenith.Packets;

/// <summary>
/// Wire item stack DTO (domain maps via Protocol). One type, three encode shapes —
/// packet <c>Encode</c> picks the writer; Gameplay never calls these. <paramref name="Damage"/> is the wear of a
/// durable item (ADR §148): null for an item that does not wear, otherwise the <c>Damage</c> NBT tag of the extra
/// data — written even at 0, as the stock BDS does.
/// </summary>
readonly record struct NetworkItemStack(short NetworkId, ushort Count, int BlockRuntimeId, int Meta = 0, int StackNetworkId = 0, bool IsShield = false, int? Damage = null)
{
    /// <summary>The one item whose wire extra data has the trailing blocking tick.</summary>
    public const string ShieldName = "minecraft:shield";

    public static NetworkItemStack Empty => new(0, 0, 0);

    /// <summary>
    /// ItemV4 (protocol 2168+, ADR §87) — i16 LE network id + count + meta + optional stack net
    /// id + block runtime id + extra blob (<see cref="WriteExtraData"/>; empty for air). Use from:
    /// InventoryContent, MobEquipment, MobArmorEquipment.
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
        if (NetworkId == 0)
            writer.WriteUnsignedVarInt(0); // air: no extra data
        else
            WriteExtraData(ref writer);
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
    /// The extra blob of a non-air stack, as the stock BDS writes it (captured from BDS 1.26.63.1, ADR §148): an
    /// <c>i16</c> NBT marker — 0 for none, or -1 followed by <c>u8 1</c> and a little-endian NBT compound — then the
    /// <c>u32</c> can-place-on and can-destroy counts (0 here: not modelled), then the <c>i64</c> blocking tick that
    /// only the shield has. Length-prefixed, so a reader that does not know a field still skips the right bytes.
    /// </summary>
    private void WriteExtraData(ref BinaryStream writer)
    {
        var extra = new List<byte>(36);
        if (Damage is { } damage)
        {
            var root = new NbtCompound();
            root.Set("Damage", NbtTag.Int(damage));
            extra.AddRange(new byte[] { 0xFF, 0xFF, 1 });
            var nbt = new NbtWriter(NbtEncoding.LittleEndian);
            nbt.WriteNamedTag(string.Empty, NbtTag.Compound(root));
            extra.AddRange(nbt.ToArray());
        }
        else
        {
            extra.AddRange(new byte[2]); // no NBT
        }

        extra.AddRange(new byte[8]); // can_place_on and can_destroy: both empty
        if (IsShield)
            extra.AddRange(new byte[8]); // blocking_tick

        writer.WriteUnsignedVarInt(extra.Count);
        writer.Write(extra.ToArray());
    }

    /// <summary>
    /// ItemStack without stack net id (VarInt network id + block runtime + extra blob).
    /// Use from: CreativeContent, CraftingData outputs. A shield (<see cref="IsShield"/>) carries the extra
    /// trailing <c>blocking_tick</c> (i64) that every Bedrock reader expects for that one item id.
    /// </summary>
    public void WriteItemStack(ref BinaryStream writer)
    {
        writer.WriteVarInt(NetworkId);
        writer.WriteUShort(Count, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Meta);
        writer.WriteVarInt(BlockRuntimeId);

        // Protocol 2193 writes the complete descriptor even for air. Only its user-data
        // buffer is empty; omitting the preceding fields consumes the next entry instead.
        if (NetworkId == 0)
        {
            writer.WriteUnsignedVarInt(0);
            return;
        }

        WriteExtraData(ref writer);
    }
}
