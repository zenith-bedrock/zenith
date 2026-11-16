using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;
using Xunit;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

public class SolidBuildingBlockTests
{
    [Fact]
    public void Every_supported_name_resolves_to_a_placeable_solid_with_a_dig_profile()
    {
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        foreach (var name in SolidBuildingBlocks.Names)
        {
            Assert.True(palette.TryGet(name, out var id), name);
            Assert.True(Blocks.IsPlaceable(id), name);
            Assert.True(Blocks.BlocksMovement(id), name);
            Assert.True(DigProfiles.TryGet(id, out var profile), name);
            Assert.True(profile.DestroySpeed > 0, name);
        }
        Assert.False(Blocks.IsPlaceable(palette.Require("minecraft:furnace")));
        Assert.False(Blocks.IsPlaceable(palette.Require("minecraft:wooden_door")), "doors still need two-block placement and toggling");
    }

    [Theory]
    [InlineData("spruce_planks", "wooden_axe")]
    [InlineData("red_wool", "wooden_axe")]
    [InlineData("blue_concrete", "wooden_pickaxe")]
    [InlineData("yellow_terracotta", "wooden_pickaxe")]
    [InlineData("polished_andesite", "wooden_pickaxe")]
    [InlineData("brick_block", "wooden_pickaxe")]
    public void Survival_place_then_break_conserves_the_block_through_floor_loot(string name, string tool)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("family-builder");
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        var id = palette.Require("minecraft:" + name);
        player.Inventory.Clear(); Assert.True(player.Inventory.TrySetBlock(0, id, 2));
        StandForPlace(player, 5, 70, 5);
        var system = new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability);
        player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, id, 0, StackId.FromBlock(id)));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(id, fx.World.GetBlock(5, 70, 5)); Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.True(player.Inventory.TrySetItem(1, fx.Context.ItemPalette.Require("minecraft:" + tool), 1));
        player.SelectedHotbarSlot = 1;
        QueueReadyBreak(fx.Clock, fx.World, player, 5, 70, 5);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(5, 70, 5));
        var drop = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(StackId.FromBlock(id), drop.Id); Assert.Equal(1, drop.Count);
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Wrong_tool_breaks_concrete_without_harvesting_it()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("concrete-hand");
        var id = BlockPaletteLoader.FromEmbeddedResource().Require("minecraft:red_concrete");
        player.Inventory.Clear(); StandForPlace(player, 5, 70, 5);
        fx.World.SetBlock(5, 70, 5, id);
        QueueReadyBreak(fx.Clock, fx.World, player, 5, 70, 5);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(5, 70, 5));
        Assert.Empty(fx.World.FloorDrops.Snapshot());
    }

    [Fact]
    public void Rejected_occupied_and_obstructed_placements_do_not_consume_new_family_stacks()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("family-obstruction");
        var id = BlockPaletteLoader.FromEmbeddedResource().Require("minecraft:blue_concrete");
        player.Inventory.Clear(); player.Inventory.TrySetBlock(0, id, 2);
        StandForPlace(player, 5, 70, 5); fx.World.SetBlock(5, 70, 5, Blocks.Stone);
        player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, id, 0));
        player.Intents.SubmitBlockEdit(BlockEditIntent.Set(6, 70, 5, id, 0));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(5, 70, 5));
        Assert.Equal(Blocks.Air, fx.World.GetBlock(6, 70, 5));
        Assert.Equal(2, player.Inventory.Get(0).Count);
    }
}
