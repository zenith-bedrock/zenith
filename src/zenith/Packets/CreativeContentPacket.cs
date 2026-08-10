using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// CreativeContent (0x91) — groups + items for creative inventory UI.
/// DTOs only; assembled from CreativeCatalog in Protocol (ADR §38).
/// </summary>
sealed class CreativeContentPacket : DataPacket
{
    /// <summary>Creative inventory category: Construction (wire enum value 1).</summary>
    public const int CategoryConstruction = 1;

    public override int Id => (int)ProtocolInfo.CREATIVE_CONTENT_PACKET;

    public CreativeGroupEntry[] Groups { get; set; } = [];
    public CreativeItemEntry[] Items { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);

        writer.WriteUnsignedVarInt(Groups.Length);
        foreach (var group in Groups)
        {
            // category is a mapper<u8> on the wire — one byte, not a fixed int32 (was
            // over-writing 3 extra bytes per group and misaligning everything after it).
            writer.WriteByte((byte)group.Category);
            writer.WriteVarString(group.Name);
            group.Icon.WriteItemStack(ref writer);
        }

        writer.WriteUnsignedVarInt(Items.Length);
        foreach (var item in Items)
        {
            // entry_id / group_index are unsigned varint on the wire (protodef "varint", not
            // "zigzag32") — unchanged from before this ADR's audit.
            writer.WriteUnsignedVarInt((int)item.CreativeItemNetworkId);
            item.Item.WriteItemStack(ref writer);
            writer.WriteUnsignedVarInt((int)item.GroupIndex);
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}

readonly record struct CreativeGroupEntry(int Category, string Name, NetworkItemStack Icon);

readonly record struct CreativeItemEntry(uint CreativeItemNetworkId, NetworkItemStack Item, uint GroupIndex);
