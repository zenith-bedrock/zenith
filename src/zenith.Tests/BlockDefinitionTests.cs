using Xunit;
using Zenith.Tests.Generators;
using Zenith.World;

namespace Zenith.Tests;

public class BlockDefinitionTests
{
    /// <summary>
    /// Maintenance entry point, not a check: <c>ZENITH_REGEN_BLOCK_DEFINITIONS=&lt;path to data/block_definitions.json&gt;</c>
    /// adds rows for palette blocks the file does not list yet (existing rows are never changed).
    /// </summary>
    [Fact]
    public void Regenerate_when_asked()
    {
        var path = Environment.GetEnvironmentVariable(BlockDefinitionGenerator.OutputVariable);
        if (string.IsNullOrEmpty(path)) return;
        BlockDefinitionGenerator.Merge(path, BlockDefinitionGenerator.Generate(BlockPaletteLoader.FromEmbeddedResource()));
    }

    [Fact]
    public void Every_definition_names_a_block_of_the_palette()
    {
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        var stale = BlockDefinitionLoader.FromEmbeddedResource().Keys.Where(name => !palette.TryGet(name, out _)).ToList();
        Assert.True(stale.Count == 0, "definitions for blocks the palette no longer has: " + string.Join(", ", stale.Take(10)));
    }

    [Fact]
    public void The_catalog_accepts_the_shipped_definitions_and_supports_the_expected_breadth()
    {
        Blocks.EnsureLoaded();
        var supported = Blocks.Catalog.States.Families.Count(f => f.Kind != BlockKind.Unsupported);
        Assert.True(supported > 700, $"expected broad coverage, got {supported}");
    }

    [Fact]
    public void A_definition_whose_block_lacks_the_kinds_state_is_refused_when_the_catalog_is_built()
    {
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        var bad = new Dictionary<string, BlockDefinition> { ["minecraft:stone"] = new(BlockKind.Door) }.ToFrozenDictionaryForTest();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new BlockCatalog(palette, BlockPropertiesLoader.FromEmbeddedResource(), bad));
        Assert.Contains("minecraft:stone", ex.Message);
        Assert.Contains("upper_block_bit", ex.Message);
    }

    [Fact]
    public void A_slab_without_a_merge_target_is_refused()
    {
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        var bad = new Dictionary<string, BlockDefinition> { ["minecraft:oak_slab"] = new(BlockKind.Slab) }.ToFrozenDictionaryForTest();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new BlockCatalog(palette, BlockPropertiesLoader.FromEmbeddedResource(), bad));
        Assert.Contains("mergesInto", ex.Message);
    }

    [Fact]
    public void Blocks_the_file_does_not_list_are_unsupported_and_undiggable()
    {
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        var catalog = new BlockCatalog(
            palette, BlockPropertiesLoader.FromEmbeddedResource(),
            new Dictionary<string, BlockDefinition>().ToFrozenDictionaryForTest());
        Assert.Equal(BlockKind.Unsupported, catalog.KindOf(palette.Require("minecraft:stone")));
        Assert.False(catalog.TryGetDigProfile(palette.Require("minecraft:stone"), out _));
    }

    [Fact]
    public void Drops_come_from_the_definition_rows()
    {
        BlockLoot.EnsureLoaded();
        Assert.Equal(StackId.FromBlock(Blocks.Palette.Require("minecraft:cobblestone")), BlockLoot.DropOf(Blocks.Stone).Id);
        Assert.Equal(0, BlockLoot.DropOf(Blocks.Palette.Require("minecraft:glass")).Count);
        Assert.Equal(2, BlockLoot.DropOf(Blocks.Palette.Require("minecraft:oak_double_slab")).Count);
        Assert.True(BlockLoot.DropOf(Blocks.CoalOre).Id.IsItem);
    }
}

file static class FrozenTestExtensions
{
    public static System.Collections.Frozen.FrozenDictionary<string, BlockDefinition> ToFrozenDictionaryForTest(
        this Dictionary<string, BlockDefinition> map) =>
        System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(map);
}
