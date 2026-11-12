using System.Text;
using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.World;

namespace Zenith.Tests;

public class ItemDurabilityTests
{
    private static readonly ItemPalette Palette = ItemPaletteLoader.FromEmbeddedResource();

    private static ItemDurability Read(string json) => ItemDurability.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(json)), Palette);

    [Fact]
    public void The_embedded_table_loads_and_knows_the_classic_tools()
    {
        var table = ItemDurability.CreateDefault(Palette);

        Assert.True(table.Count > 50);
        Assert.True(table.TryGetMax(StackId.FromItem(Palette.Require("minecraft:iron_pickaxe")), out var iron));
        Assert.Equal(250, iron);
        Assert.True(table.TryGetMax(StackId.FromItem(Palette.Require("minecraft:diamond_sword")), out var diamond));
        Assert.Equal(1561, diamond);
        Assert.False(table.TryGetMax(StackId.FromItem(Palette.Require("minecraft:stick")), out _));
    }

    [Fact]
    public void Blocks_never_wear_even_when_their_runtime_id_collides_with_an_item_id()
    {
        Blocks.EnsureLoaded();
        var table = Read("""{"items":{"minecraft:iron_pickaxe":250}}""");

        Assert.False(table.TryGetMax(StackId.FromBlock(Palette.Require("minecraft:iron_pickaxe")), out _));
    }

    [Fact]
    public void Unknown_names_non_positive_values_and_duplicates_fail_the_load()
    {
        Assert.Throws<InvalidDataException>(() => Read("""{"items":{"minecraft:not_an_item":5}}"""));
        Assert.Throws<InvalidDataException>(() => Read("""{"items":{"minecraft:iron_pickaxe":0}}"""));
        Assert.Throws<InvalidDataException>(() => Read("""{"items":{"minecraft:iron_pickaxe":5,"minecraft:iron_pickaxe":6}}"""));
    }
}
