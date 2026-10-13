using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public class ItemPaletteTests
{
    [Fact]
    public void Stable_2193_registry_preserves_updated_ids_and_component_definitions()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        Assert.Equal(2076, palette.Count);
        Assert.Equal(-1220, palette.Require("minecraft:red_concrete_slab"));
        Assert.Equal(668, palette.Require("minecraft:bamboo_raft"));
        Assert.Equal(862, palette.Require("minecraft:blue_bundle"));
        Assert.Equal(78, palette.Entries.Count(entry => entry.Components is not null));
        var spear = Assert.Single(palette.Entries, entry => entry.Name == "minecraft:stone_spear");
        Assert.NotNull(spear.Components);
        Assert.True(spear.Components.TryGet("components", out var components));
        Assert.True(components.TryGetCompound(out var definitions));
        Assert.True(definitions.Count > 0);
    }

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
    public void Palette_reverse_lookup_confirms_non_tool_item_identity()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var bone = palette.Require("minecraft:bone");

        Assert.True(palette.TryGetName(bone, out var name));
        Assert.Equal("minecraft:bone", name);
        Assert.False(palette.TryGetName(short.MaxValue + 1, out _));
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
