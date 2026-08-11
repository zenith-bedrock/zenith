using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Death must never clear only the subset of inventory that found floor-drop capacity.</summary>
public class DeathLootAtomicityTests
{
    public DeathLootAtomicityTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Void_death_moves_every_inventory_and_craft_source_as_one_conserved_commit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("void-loot");
        ClearInventory(player);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 3));
        Assert.True(player.Inventory.TrySetBlock(PlayerInventory.CursorSlot, Blocks.Stone, 2));
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 4)));
        Assert.True(player.CraftUi.TrySetResult(InventorySlot.OfBlock(Blocks.OakPlanks, 1)));

        const int expectedQuantity = 10;
        SubmitVoidMove(player);
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal("generic", player.DeathCause);
        Assert.Equal(0f, player.Health);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.True(player.CraftUi.GetGrid(0).IsEmpty);
        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(expectedQuantity, fx.World.FloorDrops.Snapshot().Sum(drop => drop.Count));
    }

    [Fact]
    public void Void_death_with_capacity_for_only_the_first_stack_keeps_all_sources_and_creates_no_drop()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("atomic-void-loot");
        ClearInventory(player);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 3));
        Assert.True(player.Inventory.TrySetBlock(1, Blocks.Stone, 2));

        // One free cell would allow the first stack to be deposited, but not the second. A death
        // drop plan must reject before it commits either source or floor-drop cell.
        for (var i = 0; i < FloorDropStore.SoftCap - 1; i++)
        {
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(
                10_000 + i, 64, 0, StackId.FromBlock(Blocks.OakLog), 1,
                entityRuntimeIdIfNew: i + 1, out _));
        }

        SubmitVoidMove(player);
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(3, player.Inventory.Get(0).Count);
        Assert.Equal(2, player.Inventory.Get(1).Count);
        Assert.Equal(FloorDropStore.SoftCap - 1, fx.World.FloorDrops.Count);
        Assert.Equal(FloorDropStore.SoftCap - 1, fx.World.FloorDrops.Snapshot().Sum(drop => drop.Count));
    }

    private static void ClearInventory(Player.Player player)
    {
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(PlayerInventory.CursorSlot, Blocks.Air, 0));
    }

    private static void SubmitVoidMove(Player.Player player) =>
        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 0f,
            yaw: 0f));
}
