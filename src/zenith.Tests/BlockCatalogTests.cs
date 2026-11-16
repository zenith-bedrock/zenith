using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public class BlockCatalogTests
{
    private static int Id(string name)
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId(name, out var id), name);
        return id;
    }

    [Theory]
    [InlineData("minecraft:oak_stairs", "Stairs")]
    [InlineData("minecraft:stone_stairs", "Stairs")]
    [InlineData("minecraft:oak_slab", "Slab")]
    [InlineData("minecraft:oak_double_slab", "DoubleSlab")]
    [InlineData("minecraft:oak_log", "Pillar")]
    [InlineData("minecraft:glass", "Cube")]
    [InlineData("minecraft:blue_stained_glass_pane", "Cube")]
    [InlineData("minecraft:dandelion", "Plant")]
    [InlineData("minecraft:oak_sapling", "Plant")]
    [InlineData("minecraft:wooden_door", "Door")]
    [InlineData("minecraft:water", "Unsupported")]
    [InlineData("minecraft:furnace", "Unsupported")]
    public void Blocks_are_classified_by_state_signature_and_name(string name, string expected) =>
        Assert.Equal(Enum.Parse<BlockKind>(expected), Blocks.Catalog.KindOf(name));

    [Fact]
    public void Placeable_set_grew_well_beyond_the_curated_list()
    {
        var placeable = 0;
        foreach (var name in AllNames())
            if (Blocks.Catalog.IsPlaceable(Id(name))) placeable++;
        Assert.True(placeable > 600, $"expected broad coverage, got {placeable}");
    }

    [Fact]
    public void Derived_dig_profiles_use_the_right_tool_and_tier()
    {
        AssertDig("minecraft:oak_stairs", ToolKind.Axe, false, ToolTier.None);
        AssertDig("minecraft:stone_stairs", ToolKind.Pickaxe, true, ToolTier.Wood);
        AssertDig("minecraft:sandstone", ToolKind.Pickaxe, true, ToolTier.Wood);
        AssertDig("minecraft:clay", ToolKind.Shovel, false, ToolTier.None);
        AssertDig("minecraft:glass", ToolKind.None, false, ToolTier.None);
        AssertDig("minecraft:glowstone", ToolKind.None, false, ToolTier.None);
        AssertDig("minecraft:cobbled_deepslate", ToolKind.Pickaxe, true, ToolTier.Wood);
        AssertDig("minecraft:diamond_block", ToolKind.Pickaxe, true, ToolTier.Iron);
        AssertDig("minecraft:iron_block", ToolKind.Pickaxe, true, ToolTier.Stone);
        AssertDig("minecraft:obsidian", ToolKind.Pickaxe, true, ToolTier.Diamond);
        Assert.False(DigProfiles.TryGet(Id("minecraft:bedrock"), out _), "unbreakable stays undiggable");
        Assert.False(DigProfiles.TryGet(Id("minecraft:furnace"), out _), "unsupported blocks are not derived");
    }

    [Fact]
    public void Hardness_comes_from_the_imported_properties()
    {
        Assert.True(Blocks.Catalog.TryGetProperty("minecraft:obsidian", out var obsidian));
        Assert.Equal(50, obsidian.Hardness);
        Assert.True(DigProfiles.TryGet(Id("minecraft:clay"), out var clay));
        Assert.Equal(0.6, clay.DestroySpeed, 3);
    }

    [Fact]
    public void Stairs_take_direction_from_yaw_and_half_from_the_cursor()
    {
        var held = Id("minecraft:oak_stairs");
        Assert.True(BlockShapes.TryResolve(held, 1, 0.5f, 0f, out var south));      // yaw 0 = looking south
        Assert.True(BlockShapes.TryResolve(held, 1, 0.5f, 90f, out var west));
        Assert.NotEqual(south, west);
        Assert.True(Blocks.Palette.TryGetState(south, out var s));
        Assert.Equal("2", s.Get("weirdo_direction"));
        Assert.Equal("0", s.Get("upside_down_bit"));

        Assert.True(BlockShapes.TryResolve(held, 2, 0.9f, 0f, out var upper)); // side face, cursor high
        Assert.True(Blocks.Palette.TryGetState(upper, out var u));
        Assert.Equal("1", u.Get("upside_down_bit"));
        Assert.True(BlockShapes.TryResolve(held, 0, 0.2f, 0f, out var ceiling));  // under a block
        Assert.True(Blocks.Palette.TryGetState(ceiling, out var c));
        Assert.Equal("1", c.Get("upside_down_bit"));
    }

    [Fact]
    public void Slabs_logs_and_torches_resolve_their_states_from_the_face()
    {
        Assert.True(BlockShapes.TryResolve(Id("minecraft:oak_slab"), 1, 0.5f, 0f, out var bottom));
        Assert.True(BlockShapes.TryResolve(Id("minecraft:oak_slab"), 0, 0.5f, 0f, out var top));
        Assert.True(Blocks.Palette.TryGetState(bottom, out var b));
        Assert.True(Blocks.Palette.TryGetState(top, out var t));
        Assert.Equal("bottom", b.Get("minecraft:vertical_half"));
        Assert.Equal("top", t.Get("minecraft:vertical_half"));

        Assert.True(BlockShapes.TryResolve(Id("minecraft:oak_log"), 4, 0.5f, 0f, out var log));
        Assert.True(Blocks.Palette.TryGetState(log, out var l));
        Assert.Equal("x", l.Get("pillar_axis"));

        Assert.True(BlockShapes.TryResolve(Id("minecraft:torch"), 5, 0.5f, 0f, out var torch));
        Assert.True(Blocks.Palette.TryGetState(torch, out var tt));
        Assert.Equal("east", tt.Get("torch_facing_direction"));
        Assert.False(BlockShapes.TryResolve(Id("minecraft:torch"), 0, 0.5f, 0f, out _));
    }

    [Fact]
    public void Double_slabs_and_unsupported_blocks_cannot_be_placed_from_an_item()
    {
        Assert.False(BlockShapes.TryResolve(Id("minecraft:oak_double_slab"), 1, 0.5f, 0f, out _));
        Assert.False(BlockShapes.TryResolve(Id("minecraft:furnace"), 1, 0.5f, 0f, out _));
    }

    [Fact]
    public void Special_drops_follow_vanilla()
    {
        Assert.Equal(StackId.FromBlock(Id("minecraft:cobblestone")), BlockLoot.DropOf(Id("minecraft:stone")).Id);
        Assert.Equal(StackId.FromBlock(Id("minecraft:dirt")), BlockLoot.DropOf(Id("minecraft:grass_block")).Id);
        Assert.Equal(0, BlockLoot.DropOf(Id("minecraft:glass")).Count);
        var slabs = BlockLoot.DropOf(Id("minecraft:oak_double_slab"));
        Assert.Equal(2, slabs.Count);
        Assert.Equal(StackId.FromBlock(Id("minecraft:oak_slab")), slabs.Id);
        Assert.Equal(StackId.FromBlock(Id("minecraft:oak_planks")), BlockLoot.DropOf(Id("minecraft:oak_planks")).Id);
    }

    private static void AssertDig(string name, ToolKind tool, bool requires, ToolTier tier)
    {
        Assert.True(DigProfiles.TryGet(Id(name), out var profile), name + " has a dig profile");
        Assert.Equal(tool, profile.EffectiveTool);
        Assert.Equal(requires, profile.RequiresCorrectToolForDrops);
        Assert.Equal(tier, profile.MinHarvestTier);
    }

    private static IEnumerable<string> AllNames()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepoPaths.Source("data", "block_properties.json")));
        foreach (var block in doc.RootElement.GetProperty("blocks").EnumerateObject())
            yield return block.Name;
    }
}
