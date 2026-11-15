using System.Buffers.Binary;
using Zenith.Binary;
using Zenith.Gameplay.Inventory;
using Zenith.Nbt;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class CreativeCatalogCompletenessTests
{
    private static readonly ItemPalette Palette = ItemPaletteLoader.FromEmbeddedResource();

    [Fact]
    public void Every_bundled_entry_and_group_survives_catalog_and_wire_projection()
    {
        Blocks.EnsureLoaded();
        var source = CreativeItemsLoader.FromEmbeddedResource(Palette);
        var catalog = CreativeCatalog.CreateDefault(Palette);
        var actual = catalog.SnapshotEntries();
        Assert.Equal(source.Items.Count, actual.Count);
        Assert.Equal(actual.Count, actual.Select(i => i.NetId).Distinct().Count());
        var counts = actual.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.Count());
        foreach (var group in source.Items.GroupBy(i => i.Id)) Assert.Equal(group.Count(), counts[group.Key]);
        foreach (var entry in actual)
        {
            Assert.True(catalog.TryGet(entry.NetId, out var identity, out _));
            Assert.Equal(entry.Id, identity);
        }
        var packet = CreativeContentBuilder.Build(catalog, Palette);
        Assert.Equal(source.Items.Select(i => i.GroupIndex).Distinct().Count(), packet.Groups.Length);
        Assert.Equal(actual.Count, packet.Items.Length);
        foreach (var entry in packet.Items)
        {
            var snapshot = actual.Single(i => i.NetId == entry.CreativeItemNetworkId);
            Assert.Equal(snapshot.Id.Aux, entry.Item.Meta);
            Assert.Equal(snapshot.Id.Data.TagBase64, entry.Item.TagBase64);
        }
        Assert.True(packet.Encode().Length > 1000);
    }

    [Fact]
    public void Potion_and_enchanted_book_variants_keep_distinct_identities()
    {
        var entries = CreativeItemsLoader.FromEmbeddedResource(Palette).Items;
        var potions = entries.Where(i => i.Id.IsItem && i.Id.Value == Palette.Require("minecraft:potion")).ToArray();
        Assert.True(potions.Select(i => i.Id.Aux).Distinct().Count() > 20);
        var books = entries.Where(i => i.Id.IsItem && i.Id.Value == Palette.Require("minecraft:enchanted_book")).ToArray();
        Assert.True(books.Length > 100);
        Assert.Equal(books.Length, books.Select(i => i.Id).Distinct().Count());
        Assert.All(books, b => Assert.NotNull(b.Id.Data.TagBase64));
    }

    [Fact]
    public void Creative_book_grant_keeps_tags_in_inventory_storage_and_item_wire()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("creative-book", GameMode.Creative);
        player.Inventory.Clear(); player.Windows.OpenPlayerContainer(0, 255);
        var book = fx.Context.Creative.SnapshotEntries().First(i =>
            i.Id.IsItem && i.Id.Value == Palette.Require("minecraft:enchanted_book"));
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(700, [
            InventoryStackAction.CraftCreative(book.NetId), InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(InventorySlotReference.CraftResult, InventorySlotReference.Player(0), 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(book.Id, player.Inventory.Get(0).Id);
        var blob = player.Inventory.PackMainBlob(); Assert.Equal(SlotBlob.Version5, blob[0]);
        var restored = new PlayerInventory(); Assert.True(restored.TryLoadMainFromBlob(blob));
        Assert.Equal(player.Inventory.Get(0), restored.Get(0));
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        Assert.True(fx.World.Chests.TrySet(1, 64, 1, 0, restored.Get(0)));
        var chest = fx.World.Chests.PackBlob(1, 64, 1);
        Assert.NotNull(chest);
        Assert.True(fx.World.Chests.TryLoadFromBlob(2, 64, 1, chest));
        Assert.Equal(book.Id, fx.World.Chests.Get(2, 64, 1, 0).Id);

        var descriptor = player.Session.Protocol.Inventory.DescribeStack(book.Id, 1);
        var writer = new BinaryStream(); descriptor.WriteItemStack(ref writer);
        var reader = new BinaryStream(writer.GetBufferDisposing().ToArray());
        reader.ReadVarInt(); reader.ReadUShort(BinaryStream.Endianess.Little);
        reader.ReadUnsignedVarInt(); reader.ReadVarInt();
        var extra = reader.ReadSpan(reader.ReadUnsignedVarInt());
        Assert.Equal(new byte[] { 255, 255, 1 }, extra[..3].ToArray());
        var root = NbtCodec.Decode(extra[3..], NbtEncoding.LittleEndian).Root.Tag.AsCompound();
        Assert.True(root.TryGet("ench", out _)); reader.Dispose();
    }

    [Fact]
    public void Tagged_blob_rejects_truncation_and_bad_lengths_without_partial_mutation()
    {
        var entry = CreativeItemsLoader.FromEmbeddedResource(Palette).Items.First(i => i.Id.Data.TagBase64 is not null);
        var blob = SlotBlob.Pack([new InventorySlot(entry.Id, 1), InventorySlot.OfBlock(Blocks.Dirt, 2)]);
        var destination = new[] { InventorySlot.OfBlock(Blocks.Stone, 3), InventorySlot.OfBlock(Blocks.Stone, 3) };
        Assert.False(SlotBlob.TryUnpack(blob.AsSpan(0, blob.Length - 1), destination));
        Assert.All(destination, slot => Assert.Equal(3, slot.Count));
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(18, 4), int.MaxValue);
        Assert.False(SlotBlob.TryUnpack(blob, destination));
        Assert.All(destination, slot => Assert.Equal(3, slot.Count));
    }

    [Fact]
    public void Changing_wear_retains_other_item_tags()
    {
        var root = new NbtCompound(); root.Set("Damage", NbtTag.Int(4)); root.Set("custom", NbtTag.String("kept"));
        var tag = Convert.ToBase64String(NbtCodec.Encode(new NbtNamedTag("", NbtTag.Compound(root)), NbtEncoding.LittleEndian));
        var id = new StackId(StackKind.Item, Palette.Require("minecraft:iron_pickaxe"), 0, new ItemUserData(0, tag));
        Assert.Equal(4, id.Damage);
        var worn = id.WithDamage(5);
        Assert.Equal(5, worn.Damage); Assert.Equal(id.Data.TagBase64, worn.Data.TagBase64);
        Assert.NotEqual(id, worn);
    }
}
