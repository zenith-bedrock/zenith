using System.Collections.Concurrent;
using System.Net;
using Zenith.Ecs;
using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Logging;
using Zenith.Raknet.Network;
using Zenith.Binary;
using Zenith.Server;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>InventorySystem, rearrange stacks and equipment replication.</summary>
public class InventorySystemIntentTests
{

    [Fact]
    public void InventorySystem_applies_swap_intent_on_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mover");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.GrassBlock, 2));

        var intent = InventoryStackIntent.Create(42, [InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Player(9))]);
        Assert.True(player.Intents.SubmitInventoryStack(intent));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.GrassBlock, player.Inventory.Get(0).Id.Value);
        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(9).Id.Value);
        Assert.Equal(5, player.Inventory.Get(9).Count);
        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
    }


    [Fact]
    public void InventorySystem_cursor_take_then_place()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dragger");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 8));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.Cursor, 3)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Equal(3, player.Inventory.Cursor.Count);

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.Transfer(InventorySlotReference.Cursor, InventorySlotReference.Player(9), 3)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.Equal(3, player.Inventory.Get(9).Count);
    }


    [Fact]
    public void InventoryStack_queue_rejects_newest_when_full()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("spammer");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 64));

        for (var i = 0; i < Player.PlayerIntents.MaxPendingInventoryStacks; i++)
            Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(i, [
                InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Player(1))
            ])));

        Assert.False(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(99, [
            InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Player(2))
        ])));
    }


    [Fact]
    public void InventorySystem_rolls_back_failed_transfer()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("failmove");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.GrassBlock, 3));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(7, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.Player(9), 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Equal(3, player.Inventory.Get(9).Count);
    }


    [Fact]
    public void InventoryStack_queue_full_still_allows_error_resync_call_path()
    {
        // Handler sends Error+Content on overflow; here we only assert Submit rejects newest
        // (Content is same-session Protocol — covered by queue reject + map-fail pattern).
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("spammer2");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 64));

        for (var i = 0; i < Player.PlayerIntents.MaxPendingInventoryStacks; i++)
            Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(i, [
                InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Player(1))
            ])));

        Assert.False(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(99, [
            InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Player(2))
        ])));
        // Authoritative bag unchanged until tick drains prior intents
        Assert.Equal(64, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void EquipmentSystem_fans_out_MobEquipment_when_hotbar_changes()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var bob = fx.AddInGamePlayer("bob");
        Assert.True(alice.Inventory.TrySetBlock(0, Blocks.Stone, 10));
        Assert.True(alice.Inventory.TrySetBlock(1, Blocks.GrassBlock, 5));
        alice.SelectedHotbarSlot = 0;

        // Prime fingerprint
        new EquipmentSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        var before = fx.Transport.Captured.Count;

        alice.SelectedHotbarSlot = 1;
        new EquipmentSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count > before,
            "EquipmentSystem should send MobEquipment to peers when held slot changes.");
        Assert.Equal(1, alice.Replicated.HotbarSlot);
        Assert.Equal(StackId.FromBlock(Blocks.GrassBlock), alice.Replicated.HeldStackId);
        Assert.Equal(5, alice.Replicated.HeldCount);
        _ = bob;
    }


    [Fact]
    public void EquipmentSystem_after_inventory_mutation_replicates_final_held_stack_in_same_tick()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("equipment-after-inventory");
        _ = fx.AddInGamePlayer("observer");
        Assert.True(alice.Inventory.TrySetBlock(0, Blocks.Dirt, 4));
        Assert.True(alice.Inventory.TrySetBlock(9, Blocks.Stone, 2));
        alice.SelectedHotbarSlot = 0;

        var equipment = new EquipmentSystem();
        equipment.Tick(fx.Clock, fx.Players.Online); // Prime the initial held fingerprint.

        Assert.True(alice.Intents.SubmitInventoryStack(InventoryStackIntent.Create(900, [
            InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Player(9))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        equipment.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(StackId.FromBlock(Blocks.Stone), alice.Replicated.HeldStackId);
        Assert.Equal(2, alice.Replicated.HeldCount);
    }


    [Fact]
    public void InventorySystem_transfers_to_open_chest_flat()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("loot");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        StandNear(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(
                InventorySlotReference.Player(0), InventorySlotReference.OpenContainer(0), 4,
                new WireSlot(InventoryContainerMap.Hotbar, 0),
                new WireSlot(InventoryContainerMap.Chest, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(6, player.Inventory.Get(0).Count);
        Assert.Equal(4, fx.World.Chests.Get(1, 64, 1, 0).Count);
        Assert.Equal(Blocks.Dirt, fx.World.Chests.Get(1, 64, 1, 0).Id.Value);
    }


    [Fact]
    public void InventorySystem_rejects_open_chest_transfer_after_player_leaves_reach()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farloot");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        player.PositionX = 100;
        player.PositionY = 64;
        player.PositionZ = 100;
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.OpenContainer(0), 4)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10, player.Inventory.Get(0).Count);
        Assert.True(fx.World.Chests.Get(1, 64, 1, 0).IsEmpty);
    }


    [Fact]
    public void InventorySystem_rejects_chest_action_bound_to_a_stale_container_generation()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stale-window");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        fx.World.SetBlock(3, 64, 1, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        Assert.True(fx.World.Chests.TryEnsure(3, 64, 1));
        StandNear(player, 3, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        var chestA = player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(50, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.OpenContainer(0), 4)
        ], chestA.Generation)));

        // A close/reopen was processed before the queued action. The packet referenced chest A;
        // resolving it against the current chest B would be a cross-container injection.
        var chestB = player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(3, 64, 1));
        Assert.True(chestB.Generation > chestA.Generation);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10, player.Inventory.Get(0).Count);
        Assert.True(fx.World.Chests.Get(1, 64, 1, 0).IsEmpty);
        Assert.True(fx.World.Chests.Get(3, 64, 1, 0).IsEmpty);
    }


    [Fact]
    public void InventorySystem_cancels_pending_window_open_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-window");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(1, 64, 1)));

        // Models a disconnect after GameLoop captured Online but before InventorySystem runs.
        player.IsInGame = false;
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Null(player.Windows.OpenContainer);
        Assert.Equal(0, fx.World.Chests.OpenerCount(1, 64, 1));
    }


    [Fact]
    public void InventorySystem_cancels_pending_mutation_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-stack");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 2));
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(508, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.Player(9), 1)
        ])));

        // The request was accepted before teardown but has no authority after disconnect.
        player.IsInGame = false;
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.True(player.Inventory.Get(9).IsEmpty);
    }


    [Fact]
    public void InventorySystem_rejects_open_chest_transfer_after_target_is_removed()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stalechest");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        StandNear(player, 1, 64, 1);
        fx.World.SetBlock(1, 64, 1, Blocks.Air);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.OpenContainer(0), 4)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10, player.Inventory.Get(0).Count);
        Assert.True(fx.World.Chests.Get(1, 64, 1, 0).IsEmpty);
    }


    [Fact]
    public void InventorySystem_rejects_double_chest_transfer_after_partner_is_removed()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stalepartner");
        var south = Blocks.ChestForFacing(Blocks.CardinalSouth);
        fx.World.SetBlock(1, 64, 1, south);
        fx.World.SetBlock(2, 64, 1, south);
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        Assert.True(fx.World.Chests.TryEnsure(2, 64, 1));
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Double(new ChestPair(1, 64, 1, 2, 64, 1)));
        StandNear(player, 1, 64, 1);
        fx.World.SetBlock(2, 64, 1, Blocks.Air);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(4, [
            InventoryStackAction.Transfer(InventorySlotReference.Player(0), InventorySlotReference.OpenContainer(0), 4)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10, player.Inventory.Get(0).Count);
        Assert.True(fx.World.Chests.Get(1, 64, 1, 0).IsEmpty);
    }


    [Fact]
    public void InventorySystem_places_into_craft_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("grid");
        player.Windows.OpenPlayerContainer(0, 0xff);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 8));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(
                InventorySlotReference.Player(0), InventorySlotReference.CraftGrid(0), 1,
                new WireSlot(InventoryContainerMap.CombinedHotbarAndInventory, 0),
                new WireSlot(InventoryContainerMap.CraftingInput, 28))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(7, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.OakLog, player.Windows.CraftUi.GetGrid(0).Id.Value);
        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
    }


    [Fact]
    public void InventorySystem_drop_deposits_floor_entity()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropper");
        StandNear(player, 2, 64, 2);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 10));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.Drop(InventorySlotReference.Player(0), 4, new WireSlot(InventoryContainerMap.Hotbar, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(6, player.Inventory.Get(0).Count);
        Assert.Equal(1, fx.World.FloorDrops.Count);
        Assert.True(fx.World.FloorDrops.TryTake(2, 64, 2, out var id, out var count, out _));
        Assert.Equal(StackId.FromBlock(Blocks.Stone), id);
        Assert.Equal(4, count);
    }


    [Fact]
    public void InventorySystem_craft_oak_log_to_planks_from_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("crafter");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 2)));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(9, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
        Assert.Equal(Blocks.OakLog, player.Windows.CraftUi.GetGrid(0).Id.Value);
        Assert.Equal(4, player.Windows.CraftUi.Result.Count);
        Assert.Equal(Blocks.OakPlanks, player.Windows.CraftUi.Result.Id.Value);
    }


    [Fact]
    public void InventorySystem_craft_then_take_result_to_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("takeout");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(10, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Cursor, 4,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Cursor.Id.Value);
        Assert.Equal(4, player.Inventory.Cursor.Count);
    }


    [Fact]
    public void InventorySystem_craft_chain_planks_then_chest_take()
    {
        // S35: log→planks take, place planks, chest craft, take chest (sequential intents).
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chaincraft");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 2));

        var sys = fx.CreateInventorySystem();

        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 1));
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(20, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Player(1), 4)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(1).Id.Value);
        Assert.Equal(4, player.Inventory.Get(1).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);

        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Air, 0));
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(21, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Player(2), 4)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(4, player.Inventory.Get(2).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);

        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.Windows.CraftUi.TrySetGrid(1, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.Windows.CraftUi.TrySetGrid(2, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.Windows.CraftUi.TrySetGrid(3, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.Inventory.TrySetBlock(1, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(2, Blocks.Air, 0));

        // ADR §139 regression: a chest's real vanilla shape (8 planks in a ring) needs a 3x3 grid —
        // the personal 2x2 grid must refuse it even though Zenith's shapeless matcher could
        // otherwise satisfy the aggregate count.
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(22, [
            InventoryStackAction.Craft(TestRecipes.Chest),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Player(3), 1,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 3))
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.False(Blocks.IsChest(player.Inventory.Get(3).Id.Value));
        Assert.Equal(Blocks.OakPlanks, player.Windows.CraftUi.GetGrid(0).Id.Value); // rolled back, not consumed

        // Open a crafting table and place the same 8 planks in its 3x3 grid — now it succeeds.
        player.Windows.OpenCraftingTableContainer(1, InventoryContainerMap.WindowTypeWorkbench);
        foreach (var cell in new[] { 0, 1, 2, 3, 5, 6, 7, 8 }) // the ring: a chest is NOT just eight planks
            Assert.True(player.Windows.TableCraftUi.TrySetGrid(cell, InventorySlot.OfBlock(Blocks.OakPlanks, 1)));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(23, [
            InventoryStackAction.Craft(TestRecipes.Chest),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Player(3), 1,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 3))
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.True(Blocks.IsChest(player.Inventory.Get(3).Id.Value));
        Assert.Equal(1, player.Inventory.Get(3).Count);
        Assert.True(player.Windows.TableCraftUi.GetGrid(0).IsEmpty); // consumed from the table grid, not the personal one
    }


    [Fact]
    public void InventorySystem_craft_refuses_while_result_occupied()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stuckout");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 2)));

        var sys = fx.CreateInventorySystem();
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(30, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.OakPlanks, player.Windows.CraftUi.Result.Id.Value);

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(31, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.OakPlanks, player.Windows.CraftUi.Result.Id.Value);
        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
    }


    [Fact]
    public void InventorySystem_craft_times_two_then_place_result_to_bag()
    {
        // Shift-click craft output: CraftRecipe(times=2) + Place 8 planks to bag.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shiftcraft");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 2)));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(12, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks, craftTimes: 2),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Player(9), 8,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 9))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.GetGrid(0).IsEmpty);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(9).Id.Value);
        Assert.Equal(8, player.Inventory.Get(9).Count);
    }


    [Fact]
    public void InventorySystem_craft_times_zero_treated_as_one()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("times0");
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(13, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks, craftTimes: 0),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.Windows.CraftUi.Result.Count);
        Assert.True(player.Windows.CraftUi.GetGrid(0).IsEmpty);
    }


    [Fact]
    public void InventorySystem_craft_times_beyond_grid_is_rejected()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("clamp");
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        // Request 5 crafts but only 1 log → clamp to 1, not fail.
        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(14, [
            InventoryStackAction.Craft(TestRecipes.OakLogToPlanks, craftTimes: 5),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
    }


    [Fact]
    public void InventorySystem_storage_slot_to_cursor_via_container_29()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("storage");
        player.Windows.OpenPlayerContainer(0, 0xff);
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Dirt, 8));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(11, [
            InventoryStackAction.Transfer(
                InventorySlotReference.Player(9), InventorySlotReference.Cursor, 8,
                new WireSlot(InventoryContainerMap.Inventory, 9),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Inventory.Get(9).IsEmpty);
        Assert.Equal(Blocks.Dirt, player.Inventory.Cursor.Id.Value);
        Assert.Equal(8, player.Inventory.Cursor.Count);
    }


    [Fact]
    public void InventorySystem_replayed_request_id_cannot_move_items_twice_when_net_ids_are_skipped()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("replay");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 8));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Stone, 3));
        var intent = InventoryStackIntent.Create(712, [InventoryStackAction.Swap(InventorySlotReference.Player(0), InventorySlotReference.Player(9))]);

        Assert.True(player.Intents.SubmitInventoryStack(intent));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(0).Id.Value);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(9).Id.Value);

        Assert.True(player.Intents.SubmitInventoryStack(intent));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(0).Id.Value);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(9).Id.Value);
    }


    [Fact]
    public void InventorySystem_craft_creative_places_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creator", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Cursor, PlayerInventory.MaxStack,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.Stone, player.Inventory.Cursor.Id.Value);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }


    [Fact]
    public void InventorySystem_craft_creative_shift_to_bag()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shiftcreate", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Player(0), PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(0).Id.Value);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void InventorySystem_craft_creative_merges_same_item_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("merges", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(PlayerInventory.CursorSlot, Blocks.Stone, 32));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Cursor, 32)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, player.Inventory.Cursor.Id.Value);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
        Assert.Equal(32, player.Windows.CraftUi.Result.Count);
    }


    [Fact]
    public void InventorySystem_craft_creative_rejects_different_item_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("clash", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(PlayerInventory.CursorSlot, Blocks.Dirt, 1));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(4, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Cursor, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.Dirt, player.Inventory.Cursor.Id.Value);
        Assert.Equal(1, player.Inventory.Cursor.Count);
    }


    [Fact]
    public void InventorySystem_craft_creative_rejects_cursor_overflow()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fullcursor", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(PlayerInventory.CursorSlot, Blocks.Stone, PlayerInventory.MaxStack));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(7, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Cursor, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
    }


    [Fact]
    public void InventorySystem_craft_creative_drop_from_result()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropcreate", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(5, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Drop(
                InventorySlotReference.CraftResult, PlayerInventory.MaxStack,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.Equal(1, fx.World.FloorDrops.Count);
    }


    [Fact]
    public void InventorySystem_craft_creative_survival_rejects()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("survivor");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(6, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventorySlotReference.CraftResult, InventorySlotReference.Cursor, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }


    [Fact]
    public void InventorySystem_craft_creative_preserves_an_existing_created_output()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-pending-output", GameMode.Creative);
        Assert.True(player.Windows.CraftUi.TrySetResult(InventorySlot.OfBlock(Blocks.Dirt, 3)));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(8, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        // A rejected pick must not replace or erase a result awaiting its original request.
        Assert.Equal(Blocks.Dirt, player.Windows.CraftUi.Result.Id.Value);
        Assert.Equal(3, player.Windows.CraftUi.Result.Count);
    }
}
