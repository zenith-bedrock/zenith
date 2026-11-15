using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class ItemStackCapacityTests
{
    [Theory]
    [InlineData("diamond_sword", 1)]
    [InlineData("water_bucket", 1)]
    [InlineData("potion", 1)]
    [InlineData("bucket", 16)]
    [InlineData("honey_bottle", 16)]
    [InlineData("oak_sign", 16)]
    [InlineData("snowball", 16)]
    [InlineData("diamond", 64)]
    public void Add_splits_into_legal_stacks_and_preserves_quantity(string name, int limit)
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var id = StackId.FromItem(palette.Require("minecraft:" + name));
        var inventory = new PlayerInventory(false);
        Assert.Equal(limit, ItemStackLimits.For(id));
        Assert.True(inventory.TryAdd(id, limit + 1));
        Assert.Equal(limit, inventory.Get(0).Count);
        Assert.Equal(1, inventory.Get(1).Count);
        Assert.Equal(limit + 1, inventory.SnapshotMainInventory().Sum(s => s.Count));
    }

    [Fact]
    public void Block_item_shulker_boxes_do_not_merge_in_inventory_or_on_floor()
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:white_shulker_box", out var rid));
        var id = StackId.FromBlock(rid);
        var inventory = new PlayerInventory(false);
        Assert.Equal(1, ItemStackLimits.For(id));
        Assert.True(inventory.TryAdd(id, 2));
        Assert.Equal(1, inventory.Get(0).Count); Assert.Equal(1, inventory.Get(1).Count);
        var floor = new FloorDropStore();
        Assert.True(floor.TryAddOrMerge(0, 64, 0, id, 1, 1, out _));
        Assert.False(floor.TryAddOrMerge(0, 64, 0, id, 1, 2, out _));
        Assert.Equal(1, Assert.Single(floor.Snapshot()).Count);
    }

    [Fact]
    public void Full_inventory_add_refuses_without_partial_changes()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var sword = StackId.FromItem(palette.Require("minecraft:diamond_sword"));
        var inventory = new PlayerInventory(false);
        for (var i = 1; i < PlayerInventory.FullInventorySize; i++) inventory.TrySetBlock(i, Blocks.Stone, 64);
        Assert.False(inventory.TryAdd(sword, 2));
        Assert.True(inventory.Get(0).IsEmpty);
        Assert.Equal(1, inventory.TryAddUpTo(sword, 2));
        Assert.Equal(1, inventory.Get(0).Count);
    }

    [Fact]
    public void Cursor_and_chest_transfers_reject_over_capacity_and_roll_back()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("stack-transfer");
        player.Inventory.Clear();
        var bucket = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:bucket"));
        Assert.True(player.Inventory.TrySet(0, bucket, 16));
        Assert.True(player.Inventory.TrySet(PlayerInventory.CursorSlot, bucket, 16));
        player.Windows.OpenPlayerContainer(0, 255);
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1301, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.Cursor, 1)]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16, player.Inventory.Get(0).Count); Assert.Equal(16, player.Inventory.Cursor.Count);
        Assert.False(player.Inventory.TrySet(1, bucket, 17));
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        Assert.False(fx.World.Chests.TrySet(1, 64, 1, 0, new InventorySlot(bucket, 17)));
        Assert.True(fx.World.Chests.Get(1, 64, 1, 0).IsEmpty);
        player.Windows.CraftUi.TrySetGrid(0, new InventorySlot(bucket, 16));
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1303, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.CraftGrid(1), 1),
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.CraftGrid(0), 1)]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16, player.Inventory.Get(0).Count);
        Assert.Equal(16, player.Windows.CraftUi.GetGrid(0).Count);
        Assert.True(player.Windows.CraftUi.GetGrid(1).IsEmpty);
    }

    [Theory]
    [InlineData("diamond_sword", 1)]
    [InlineData("bucket", 16)]
    [InlineData("diamond", 64)]
    public void Creative_grants_one_legal_stack(string name, int count)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("stack-creative", GameMode.Creative);
        player.Windows.OpenPlayerContainer(0, 255);
        var id = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:" + name));
        var entry = fx.Context.Creative.SnapshotEntries().First(e => e.Id == id);
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1302, [
            InventoryStackAction.CraftCreative(entry.NetId), InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(InventorySlotReference.CraftResult, InventorySlotReference.Player(0), count)]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(new InventorySlot(id, count), player.Inventory.Get(0));
        Assert.False(player.Windows.CraftUi.HasPendingOutputs);
    }

    [Fact]
    public void Recipe_batch_refuses_nonstackable_results_before_consuming()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("batch-tools");
        player.Inventory.Clear(); player.Windows.OpenCraftingTableContainer(1, 1);
        var diamond = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:diamond"));
        var stick = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:stick"));
        player.Inventory.TrySet(0, diamond, 4); player.Inventory.TrySet(1, stick, 2);
        var recipe = fx.Context.Recipes.Recipes.First(r => r.Outputs.Any(o => o.Stack ==
            StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:diamond_sword"))));
        Assert.False(fx.Context.Recipes.TryPlanAutoCraft(recipe.NetId, 2,
            player.Inventory.SnapshotMainInventory(), true, out _, out _));
        Assert.Equal(4, player.Inventory.Get(0).Count); Assert.Equal(2, player.Inventory.Get(1).Count);
    }

    [Fact]
    public void Legacy_oversized_stack_can_drain_but_cannot_move_or_grow_oversized()
    {
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var sword = StackId.FromItem(palette.Require("minecraft:diamond_sword"));
        var slots = new InventorySlot[PlayerInventory.FullInventorySize]; slots[0] = new(sword, 4);
        var inventory = new PlayerInventory(false);
        Assert.True(inventory.TryLoadMainFromBlob(SlotBlob.Pack(slots)));
        Assert.False(inventory.TrySwap(0, 1));
        Assert.False(inventory.TrySet(0, sword, 5));
        Assert.True(inventory.TryTransfer(0, 1, 1));
        Assert.Equal(3, inventory.Get(0).Count); Assert.Equal(1, inventory.Get(1).Count);
        Assert.True(inventory.TrySet(0, sword, 2));
    }

    [Fact]
    public void Floor_batch_splits_legacy_stack_or_refuses_the_whole_batch()
    {
        var fx = new IntentTestFixture();
        var sword = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:diamond_sword"));
        Assert.False(FloorDropFanout.TryDeposit(fx.World, fx.Players, [], 0, 64, 0, sword, 2, searchRadius: 0));
        Assert.Empty(fx.World.FloorDrops.Snapshot());
        Assert.True(FloorDropFanout.TryDeposit(fx.World, fx.Players, [], 0, 64, 0, sword, 2));
        Assert.Equal(2, fx.World.FloorDrops.Count);
        Assert.All(fx.World.FloorDrops.Snapshot(), d => Assert.Equal(1, d.Count));
    }

    [Fact]
    public void Failed_transaction_restores_legacy_oversized_chest_custody_exactly()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("legacy-chest");
        player.Inventory.Clear(); IntentTestHelpers.StandNear(player, 1, 64, 1);
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        var sword = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:diamond_sword"));
        var slots = new InventorySlot[ChestStore.SingleSize]; slots[0] = new(sword, 4);
        Assert.True(fx.World.Chests.TryLoadFromBlob(1, 64, 1, SlotBlob.Pack(slots)));
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1304, [
            InventoryStackAction.Transfer(InventorySlotReference.OpenContainer(0), InventorySlotReference.Player(0), 1),
            InventoryStackAction.Transfer(InventorySlotReference.Player(9), InventorySlotReference.Player(1), 1)]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(new InventorySlot(sword, 4), fx.World.Chests.Get(1, 64, 1, 0));
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }
}
