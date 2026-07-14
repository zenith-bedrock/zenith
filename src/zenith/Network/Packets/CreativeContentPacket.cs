using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.World;

namespace Zenith.Network.Packets;

/// <summary>
/// CreativeContent (0x91) — groups + items for creative inventory UI.
/// Wire layout for <see cref="ServerIdentity.ProtocolVersion"/> (gophertunnel Groups + CreativeItems).
/// V1: short starter list from ItemPalette — not creative_items.json (ADR §31).
/// </summary>
sealed class CreativeContentPacket : DataPacket
{
    /// <summary>gophertunnel CreativeCategoryConstruction.</summary>
    public const int CategoryConstruction = 1;

    public override int Id => (int)ProtocolInfo.CREATIVE_CONTENT_PACKET;

    public CreativeGroupEntry[] Groups { get; set; } = [];
    public CreativeItemEntry[] Items { get; set; } = [];

    /// <summary>Anonymous construction group + starter blocks already in Blocks/ItemPalette.</summary>
    public static CreativeContentPacket CreateStarter(ItemPalette palette)
    {
        Blocks.EnsureLoaded();
        ReadOnlySpan<(string Name, int RuntimeId)> entries =
        [
            ("minecraft:stone", Blocks.Stone),
            ("minecraft:grass_block", Blocks.GrassBlock),
            ("minecraft:dirt", Blocks.Dirt),
            ("minecraft:oak_planks", Blocks.OakPlanks),
            ("minecraft:oak_log", Blocks.OakLog),
            ("minecraft:sand", Blocks.Sand),
            ("minecraft:chest", Blocks.Chest)
        ];

        var items = new CreativeItemEntry[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var (name, runtimeId) = entries[i];
            items[i] = new CreativeItemEntry(
                CreativeItemNetworkId: (uint)(i + 1),
                Item: new NetworkItemStack(palette.Require(name), 1, runtimeId),
                GroupIndex: 0);
        }

        return new CreativeContentPacket
        {
            Groups =
            [
                new CreativeGroupEntry(
                    Category: CategoryConstruction,
                    Name: "",
                    Icon: NetworkItemStack.Empty)
            ],
            Items = items
        };
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);

        writer.WriteUnsignedVarInt(Groups.Length);
        foreach (var group in Groups)
        {
            writer.WriteInt(group.Category, BinaryStream.Endianess.Little);
            writer.WriteVarString(group.Name);
            group.Icon.Write(ref writer);
        }

        writer.WriteUnsignedVarInt(Items.Length);
        foreach (var item in Items)
        {
            writer.WriteUnsignedVarInt((int)item.CreativeItemNetworkId);
            item.Item.Write(ref writer);
            writer.WriteUnsignedVarInt((int)item.GroupIndex);
        }

        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}

readonly record struct CreativeGroupEntry(int Category, string Name, NetworkItemStack Icon);

readonly record struct CreativeItemEntry(uint CreativeItemNetworkId, NetworkItemStack Item, uint GroupIndex);
