using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Inventory;

namespace Zenith.Tests;

/// <summary>
/// Characterization regressions for inventory transitions that must never mint authoritative items.
/// These test state conservation, not client UI packet shapes.
/// </summary>
public class InventoryDuplicationRegressionTests
{
    public InventoryDuplicationRegressionTests() => Blocks.EnsureLoaded();

    [Fact]
    public void InventorySystem_failed_action_after_drop_leaves_no_floor_item_and_restores_source()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("drop-rollback");
        player.PositionX = 2;
        player.PositionY = 64;
        player.PositionZ = 2;
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));
        // The second action must fail: a dirt transfer cannot overwrite this stone stack.
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Stone, 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(801, [
            InventoryStackAction.Drop(0, 4),
            InventoryStackAction.Transfer(0, 9, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(0).Id.Value);
        Assert.Equal(1, player.Inventory.Get(9).Count);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(9).Id.Value);
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void InventorySystem_drop_conserves_authoritative_item_quantity_across_player_and_world()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("drop-conservation");
        player.PositionX = 2;
        player.PositionY = 64;
        player.PositionZ = 2;
        var dirt = StackId.FromBlock(Blocks.Dirt);
        Assert.True(player.Inventory.TrySet(0, dirt, 4));
        var before = CountAuthoritativeQuantity(player, fx.World, dirt);

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(808, [
            InventoryStackAction.Drop(0, 4)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.Equal(before, CountAuthoritativeQuantity(player, fx.World, dirt));
    }

    [Fact]
    public void InventorySystem_failed_multi_drop_request_does_not_publish_any_partial_drop()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("multi-drop-rollback");
        player.PositionX = 2;
        player.PositionY = 64;
        player.PositionZ = 2;
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 4));
        Assert.True(player.Inventory.TrySetBlock(1, Blocks.Stone, 4));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.OakLog, 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(802, [
            InventoryStackAction.Drop(0, 4),
            InventoryStackAction.Drop(1, 4),
            InventoryStackAction.Transfer(9, 2, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(0).Id.Value);
        Assert.Equal(4, player.Inventory.Get(1).Count);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(1).Id.Value);
        Assert.Equal(1, player.Inventory.Get(9).Count);
        Assert.Equal(Blocks.OakLog, player.Inventory.Get(9).Id.Value);
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void InventorySystem_two_actions_overconsuming_one_source_roll_back_the_whole_request()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("double-consume");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 4));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(10, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(803, [
            InventoryStackAction.Transfer(0, 9, 2),
            InventoryStackAction.Transfer(0, 10, 3)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.Inventory.Get(0).Count);
        Assert.True(player.Inventory.Get(9).IsEmpty);
        Assert.True(player.Inventory.Get(10).IsEmpty);
    }

    [Fact]
    public void InventorySystem_source_equal_destination_is_rejected_without_mutation()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("same-slot");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 4));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(804, [
            InventoryStackAction.Transfer(0, 0, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(0).Id.Value);
    }

    [Fact]
    public void InventorySystem_full_destination_rejects_transfer_without_losing_source()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("full-destination");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 1));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Dirt, PlayerInventory.MaxStack));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(806, [
            InventoryStackAction.Transfer(0, 9, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Get(9).Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(PlayerInventory.MaxStack + 1)]
    public void InventorySystem_invalid_transfer_quantity_leaves_authoritative_slots_unchanged(int count)
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer($"invalid-quantity-{count}");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 4));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(807 + count, [
            InventoryStackAction.Transfer(0, 9, count)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.Inventory.Get(0).Count);
        Assert.True(player.Inventory.Get(9).IsEmpty);
    }

    [Fact]
    public void InventorySystem_replayed_craft_request_cannot_materialize_output_twice()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("craft-replay");
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 2)));
        var request = InventoryStackIntent.Create(805, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.CreateOutput()
        ]);

        Assert.True(player.SubmitInventoryStack(request));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.Equal(4, player.CraftUi.Result.Count);

        Assert.True(player.SubmitInventoryStack(request));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.Equal(4, player.CraftUi.Result.Count);
    }

    private static int CountAuthoritativeQuantity(Player.Player player, World.World world, StackId id)
    {
        var total = 0;
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
        {
            var slot = player.Inventory.Get(i);
            if (!slot.IsEmpty && slot.Id == id) total += slot.Count;
        }

        var cursor = player.Inventory.Cursor;
        if (!cursor.IsEmpty && cursor.Id == id) total += cursor.Count;

        for (var i = 0; i < PlayerCraftUi.GridSize; i++)
        {
            var slot = player.CraftUi.GetGrid(i);
            if (!slot.IsEmpty && slot.Id == id) total += slot.Count;
        }

        var result = player.CraftUi.Result;
        if (!result.IsEmpty && result.Id == id) total += result.Count;

        foreach (var drop in world.FloorDrops.Snapshot())
        {
            if (drop.Id == id) total += drop.Count;
        }

        return total;
    }
}
