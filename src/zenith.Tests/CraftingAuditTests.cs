using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Crafting behaviour as a real client drives it (see docs/inventory-audit.md). Passing tests pin what was
/// verified; <c>Skip</c>ped tests are reproduced gaps awaiting a fix — they must not be deleted, only un-skipped.
/// </summary>
public class CraftingAuditTests
{
    public CraftingAuditTests() => Blocks.EnsureLoaded();

    private static InventoryStackIntent CraftThenTake(int requestId, byte crafts) =>
        InventoryStackIntent.Create(requestId, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks, crafts),
            InventoryStackAction.ConsumeNoOp(),
            InventoryStackAction.Transfer(InventorySlotReference.CraftResult, InventorySlotReference.Cursor, 0),
        ], expectedOpenContainerGeneration: 0, requiresOpenContainer: false, requiredOpenContainerTarget: null);

    [Fact]
    public void Personal_craft_then_take_to_cursor_moves_exactly_one_craft_of_items()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("craft-take");
        player.OpenPlayerContainer(0, 0xff);
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 3)));

        Assert.True(player.SubmitInventoryStack(CraftThenTake(901, 1)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, player.CraftUi.GetGrid(0).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(StackId.FromBlock(Blocks.OakPlanks), player.Inventory.Cursor.Id);
        Assert.Equal(4, player.Inventory.Cursor.Count);
    }

    [Fact]
    public void Craft_request_beyond_what_the_grid_affords_is_rejected_not_downscaled()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("craft-overask");
        player.OpenPlayerContainer(0, 0xff);
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        Assert.True(player.SubmitInventoryStack(CraftThenTake(902, 3)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.True(player.Inventory.Cursor.IsEmpty);
    }
}
