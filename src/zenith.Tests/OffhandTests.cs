using Zenith.Gameplay.Inventory;
using Xunit;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// The offhand slot: a one-slot domain container (ISR container 34, slot 1; InventoryContent window 119) that is
/// owned, persisted and moved like any other slot. Conservation across it is what these pin.
/// </summary>
public class OffhandTests
{
    public OffhandTests() => Blocks.EnsureLoaded();

    private static int Total(Player.Player p) =>
        Enumerable.Range(0, PlayerInventory.FullInventorySize).Sum(i => p.Inventory.Get(i).Count)
        + p.Inventory.Cursor.Count + p.Inventory.Offhand.Count;

    // ---- domain ----------------------------------------------------------------------------------------------

    [Fact]
    public void Offhand_holds_a_stack_and_empty_normalises()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.Offhand.IsEmpty);

        Assert.True(inv.TrySetOffhand(StackId.FromBlock(Blocks.Dirt), 5));
        Assert.Equal(5, inv.Offhand.Count);

        Assert.True(inv.TrySetOffhand(StackId.FromBlock(Blocks.Air), 0));
        Assert.True(inv.Offhand.IsEmpty);
        Assert.False(inv.TrySetOffhand(StackId.FromBlock(Blocks.Dirt), PlayerInventory.MaxStack + 1));
    }

    [Fact]
    public void An_inventory_snapshot_restores_the_offhand()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetOffhand(StackId.FromBlock(Blocks.Dirt), 5));
        var snapshot = inv.CaptureSnapshot();

        Assert.True(inv.TrySetOffhand(StackId.FromBlock(Blocks.Stone), 1));
        inv.RestoreSnapshot(snapshot);

        Assert.Equal(StackId.FromBlock(Blocks.Dirt), inv.Offhand.Id);
        Assert.Equal(5, inv.Offhand.Count);
    }

    [Fact]
    public void The_offhand_blob_round_trips_and_a_bad_blob_changes_nothing()
    {
        var inv = new PlayerInventory();
        Assert.True(inv.TrySetOffhand(StackId.FromItem(300, aux: 4), 2));
        var blob = inv.PackOffhandBlob();

        var loaded = new PlayerInventory();
        Assert.True(loaded.TryLoadOffhandFromBlob(blob));
        Assert.Equal(inv.Offhand, loaded.Offhand);

        Assert.True(loaded.TrySetOffhand(StackId.FromBlock(Blocks.Dirt), 9));
        Assert.False(loaded.TryLoadOffhandFromBlob([99, 1, 2]));
        Assert.Equal(9, loaded.Offhand.Count);
    }

    [Fact]
    public async Task The_world_persists_and_reloads_the_offhand()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("lefty");
        Assert.True(player.Inventory.TrySetOffhand(StackId.FromBlock(Blocks.Dirt), 7));

        fx.World.PersistOffhand(player);
        await Task.Yield();

        var fresh = new PlayerInventory();
        Assert.True(fx.World.TryLoadOffhand(player.Uuid, fresh));
        Assert.Equal(7, fresh.Offhand.Count);
    }

    // ---- wire addressing -------------------------------------------------------------------------------------

    [Fact]
    public void Container_34_slot_1_is_the_offhand_and_nothing_else_in_it_maps()
    {
        Assert.True(InventoryContainerMap.TryMap(InventoryContainerMap.Offhand, 1, out var reference));
        Assert.Equal(InventorySlotReference.Offhand, reference);

        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.Offhand, 0, out _));
        Assert.False(InventoryContainerMap.TryMap(InventoryContainerMap.Offhand, 2, out _));

        Assert.True(InventoryContainerMap.TryToWire(InventorySlotReference.Offhand, out var container, out var slot));
        Assert.Equal((InventoryContainerMap.Offhand, (byte)1), (container, slot));
    }

    // ---- ISR -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_request_moves_a_stack_between_the_bag_and_the_offhand_without_creating_or_losing_items()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shield");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Dirt, 10));
        var before = Total(player);

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(60, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(4), InventorySlotReference.Offhand, 6),
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.Inventory.Get(4).Count);
        Assert.Equal(6, player.Inventory.Offhand.Count);
        Assert.Equal(before, Total(player));
    }

    [Fact]
    public void A_failed_request_that_touched_the_offhand_rolls_it_back()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("rollback");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Dirt, 10));
        Assert.True(player.Inventory.TrySetOffhand(StackId.FromBlock(Blocks.Dirt), 2));
        var before = Total(player);

        // First action succeeds (offhand 2 -> 8), second cannot (slot 9 is empty), so the whole request must undo.
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(61, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(4), InventorySlotReference.Offhand, 6),
            InventoryStackAction.Transfer(InventorySlotReference.Player(9), InventorySlotReference.Offhand, 1),
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10, player.Inventory.Get(4).Count);
        Assert.Equal(2, player.Inventory.Offhand.Count);
        Assert.Equal(before, Total(player));
    }

    [Fact]
    public void Stacks_that_differ_only_in_aux_do_not_merge_in_the_offhand()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("aux");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySet(4, StackId.FromItem(300, aux: 1), 3));
        Assert.True(player.Inventory.TrySetOffhand(StackId.FromItem(300, aux: 2), 1));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(62, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(4), InventorySlotReference.Offhand, 1),
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(3, player.Inventory.Get(4).Count);
        Assert.Equal(StackId.FromItem(300, aux: 2), player.Inventory.Offhand.Id);
    }
}
