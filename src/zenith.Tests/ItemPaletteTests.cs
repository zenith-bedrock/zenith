using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public class ItemPaletteTests
{
    [Fact]
    public void Embedded_palette_resolves_air_stone_grass_network_ids()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        Assert.True(palette.Count > 100);
        Assert.Equal(-158, palette.Require("minecraft:air"));
        Assert.Equal(1, palette.Require("minecraft:stone"));
        Assert.Equal(2, palette.Require("minecraft:grass_block"));
    }

    [Fact]
    public void Blocks_TryGetName_round_trips_loaded_ids()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
        Assert.True(Blocks.TryGetName(Blocks.Stone, out var stoneName));
        Assert.Equal("minecraft:stone", stoneName);
        Assert.False(Blocks.TryGetName(int.MaxValue, out _));
    }

    [Fact]
    public void Block_to_item_ids_align_for_starter_blocks()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
        var items = ItemPaletteLoader.FromEmbeddedResource();

        Assert.True(Blocks.TryGetName(Blocks.Air, out var airName));
        Assert.Equal(items.Require(airName), items.Require("minecraft:air"));
        Assert.True(Blocks.TryGetName(Blocks.Stone, out var stoneName));
        Assert.Equal(1, items.Require(stoneName));
        Assert.True(Blocks.TryGetName(Blocks.GrassBlock, out var grassName));
        Assert.Equal(2, items.Require(grassName));
    }
}
