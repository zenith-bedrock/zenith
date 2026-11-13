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

/// <summary>Block, dig, floor-drop and chunk-overlay intents: queued edits applied on the GameLoop tick.</summary>
public class BlockIntentTests
{

    [Fact]
    public void BlockSystem_drains_fifo_queue_in_one_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("builder");
        // Stand on top of the middle cell (Y+1): in reach of 1–3, no body ∩ place cells at Y=64.
        StandNear(player, 2, 65, 0);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 10));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 0, Blocks.Stone, hotbarSlot: 0)));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Intents.TryConsumeBlockEdit(out _));
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(1, 64, 0));
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(2, 64, 0));
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(3, 64, 0));
        Assert.Equal(7, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_place_consumes_HotbarSlot_from_intent_not_SelectedHotbarSlot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("swapper");
        StandForPlace(player, 5, 70, 5);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySetBlock(2, Blocks.GrassBlock, 5));
        player.SelectedHotbarSlot = 0; // network race: client swapped selection after submit

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, Blocks.GrassBlock, hotbarSlot: 2)));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.GrassBlock, fx.World.GetBlock(5, 70, 5));
        Assert.Equal(5, player.Inventory.Get(0).Count); // slot 0 untouched
        Assert.Equal(4, player.Inventory.Get(2).Count); // intent slot consumed
    }


    [Fact]
    public void BlockSystem_rejects_place_when_queued_stack_identity_is_stale()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stale-place");
        StandForPlace(player, 5, 70, 6);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 5));

        // The network handler had observed stone, but an accepted inventory operation replaced
        // the same hotbar slot before the GameLoop reached this queued world mutation.
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(
            5, 70, 6, Blocks.Stone, hotbarSlot: 0, expectedPlacementStackId: StackId.FromBlock(Blocks.Stone))));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(5, 70, 6));
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(0).Id.Value);
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_rejects_place_into_own_body_cell_without_consuming()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("selftrap");
        StandNear(player, 6, 64, 6);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(6, 64, 6, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(6, 64, 6));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_allows_place_flush_adjacent_to_standing_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sideplace");
        StandForPlace(player, 7, 64, 7);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 3));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(7, 64, 7, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(7, 64, 7));
        Assert.Equal(2, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_rejects_place_into_peer_body_cell()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        var b = fx.AddInGamePlayer("bob");
        StandNear(a, 10, 64, 10);
        StandForPlace(b, 10, 64, 10);
        Assert.True(b.Inventory.TrySetBlock(0, Blocks.Stone, 4));

        Assert.True(b.Intents.SubmitBlockEdit(BlockEditIntent.Set(10, 64, 10, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(10, 64, 10));
        Assert.Equal(4, b.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_creative_rejects_place_into_own_body_cell()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("cself", GameMode.Creative);
        StandNear(player, 11, 64, 11);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 1));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(11, 64, 11, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(11, 64, 11));
    }


    [Fact]
    public void BlockSystem_rejects_place_when_HotbarSlot_invalid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("badslot");
        StandForPlace(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Stone, hotbarSlot: -1)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        // Place without valid slot must not mutate world or inventory.
        Assert.NotEqual(Blocks.Stone, fx.World.GetBlock(1, 64, 1));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_rejects_non_placeable_runtime_on_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("goldie");
        StandNear(player, 4, 64, 4);
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        Assert.True(palette.TryGet("minecraft:gold_block", out var goldRid));
        Assert.False(Blocks.IsPlaceable(goldRid));
        Assert.True(player.Inventory.TrySetBlock(0, goldRid, 3));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(4, 64, 4, goldRid, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.NotEqual(goldRid, fx.World.GetBlock(4, 64, 4));
        Assert.Equal(3, player.Inventory.Get(0).Count); // not consumed
    }


    [Fact]
    public void BlockSystem_rejects_place_into_occupied_cell()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stacker");
        StandNear(player, 4, 80, 4);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        fx.World.SetBlock(4, 80, 4, Blocks.GrassBlock);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(4, 80, 4, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.GrassBlock, fx.World.GetBlock(4, 80, 4));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_rejects_break_of_air()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("airpunch");
        StandNear(player, 0, 100, 0);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 1));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(0, 100, 0, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 100, 0));
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_break_fills_storage_when_hotbar_full()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fullhotbar");
        StandNear(player, 0, 90, 0);
        for (var i = 0; i < PlayerInventory.HotbarSize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.GrassBlock, PlayerInventory.MaxStack));
        EquipWoodenPickaxe(player, slot: 8);

        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        QueueReadyBreak(fx.Clock, fx.World, player, 0, 90, 0);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(Blocks.Stone, player.Inventory.Get(9).Id.Value);
        Assert.Equal(1, player.Inventory.Get(9).Count);
    }


    [Fact]
    public void BlockSystem_break_full_inventory_floor_drops()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fullbag");
        StandNear(player, 0, 90, 0);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.GrassBlock, PlayerInventory.MaxStack));
        EquipWoodenPickaxe(player, slot: 0);

        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        QueueReadyBreak(fx.Clock, fx.World, player, 0, 90, 0);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(1, fx.World.FloorDrops.Count);
    }


    [Fact]
    public void BlockSystem_break_refuses_before_mutation_when_no_loot_destination_exists()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("no-loot-destination");
        StandNear(player, 0, 90, 0);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.GrassBlock, PlayerInventory.MaxStack));
        EquipWoodenPickaxe(player, slot: 0);

        // A full bounded store cannot create or merge a stone drop. The source block must remain
        // authoritative rather than becoming air with silently lost loot.
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
        {
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(
                10_000 + i, 90, 0, StackId.FromBlock(Blocks.Dirt), 1,
                fx.Players.AllocateRuntimeId(), out _));
        }

        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        QueueReadyBreak(fx.Clock, fx.World, player, 0, 90, 0);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(FloorDropStore.SoftCap, fx.World.FloorDrops.Count);
        Assert.DoesNotContain(player.Inventory.SnapshotMainInventory(), slot =>
            !slot.IsEmpty && slot.Id == StackId.FromBlock(Blocks.Stone));
    }


    [Fact]
    public void BlockSystem_chest_break_refuses_before_mutation_when_its_contents_cannot_be_preserved()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("no-chest-loot-destination");
        StandNear(player, 1, 90, 0);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.GrassBlock, PlayerInventory.MaxStack));
        EquipWoodenPickaxe(player, slot: 0);

        for (var i = 0; i < FloorDropStore.SoftCap; i++)
        {
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(
                20_000 + i, 90, 0, StackId.FromBlock(Blocks.Dirt), 1,
                fx.Players.AllocateRuntimeId(), out _));
        }

        fx.World.SetBlock(1, 90, 0, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(1, 90, 0));
        Assert.True(fx.World.Chests.TrySet(1, 90, 0, 0, InventorySlot.OfBlock(Blocks.Stone, 1)));
        QueueReadyBreak(fx.Clock, fx.World, player, 1, 90, 0);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Chest, fx.World.GetBlock(1, 90, 0));
        Assert.Equal(Blocks.Stone, fx.World.Chests.Get(1, 90, 0, 0).Id.Value);
        Assert.Equal(1, fx.World.Chests.Get(1, 90, 0, 0).Count);
        Assert.Equal(FloorDropStore.SoftCap, fx.World.FloorDrops.Count);
    }


    [Fact]
    public void BlockSystem_stone_break_without_pickaxe_drops_nothing()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fist");
        StandNear(player, 0, 90, 0);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        var beforeFloor = fx.World.FloorDrops.Count;
        QueueReadyBreak(fx.Clock, fx.World, player, 0, 90, 0);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(beforeFloor, fx.World.FloorDrops.Count);
        Assert.Equal(0, CountRuntime(player.Inventory, Blocks.Stone));
    }


    [Fact]
    public void BlockSystem_creative_break_dumps_chest_contents_to_floor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creachest", GameMode.Creative);
        StandNear(player, 5, 64, 5);
        fx.World.SetBlock(5, 64, 5, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(5, 64, 5));
        Assert.True(fx.World.Chests.TrySet(5, 64, 5, 0, InventorySlot.OfBlock(Blocks.Dirt, 7)));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(5, 64, 5, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(5, 64, 5));
        Assert.False(fx.World.Chests.TryGetSlots(5, 64, 5, out _));
        Assert.True(fx.World.FloorDrops.Count >= 1);
        Assert.Equal(0, CountRuntime(player.Inventory, Blocks.Dirt));
    }


    [Fact]
    public void BlockSystem_partial_pickup_when_only_stack_space_remains()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("almostfull");
        // Feet on the drop cell so PickupFloorDrops reach check passes.
        StandNear(player, 5, 64, 5);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Dirt, PlayerInventory.MaxStack));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 63));

        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            5, 64, 5, StackId.FromBlock(Blocks.Dirt), 5, entityRuntimeIdIfNew: 100, out _,
            pickupDelayTicks: 0));

        new FloorDropSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(64, player.Inventory.Get(0).Count);
        Assert.Equal(1, fx.World.FloorDrops.Count);
        Assert.True(fx.World.FloorDrops.TryTake(5, 64, 5, out _, out var left, out _));
        Assert.Equal(4, left);
    }


    [Fact]
    public void BlockSystem_partial_pickup_grass_same_cell_when_stack_has_space()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("grasscell");
        // Feet in the drop cell — AABB expand (1,0.5,1) does not reach one block below.
        StandNear(player, 8, Blocks.FlatGrassY, 8);

        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.GrassBlock, PlayerInventory.MaxStack));
        Assert.True(player.Inventory.TrySetBlock(6, Blocks.GrassBlock, 62));

        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            8, Blocks.FlatGrassY, 8, StackId.FromBlock(Blocks.GrassBlock), 3, entityRuntimeIdIfNew: 200, out _,
            pickupDelayTicks: 0));

        new FloorDropSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(64, player.Inventory.Get(6).Count);
        Assert.True(fx.World.FloorDrops.TryTake(8, Blocks.FlatGrassY, 8, out _, out var left, out _));
        Assert.Equal(1, left);
    }


    [Fact]
    public void BlockSystem_partial_pickup_after_AuthInput_eye_space_converts_to_feet()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("eyepickup");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.GrassBlock, PlayerInventory.MaxStack));
        Assert.True(player.Inventory.TrySetBlock(6, Blocks.GrassBlock, 62));

        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            8, Blocks.FlatGrassY, 8, StackId.FromBlock(Blocks.GrassBlock), 3, entityRuntimeIdIfNew: 201, out _,
            pickupDelayTicks: 0));

        // Client AuthInput Y = StartGame eye-space (feet + PlayerEyeHeight); same cell as drop.
        var feetY = (float)Blocks.FlatGrassY;
        var eyeY = feetY + Blocks.PlayerEyeHeight;
        player.Intents.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            8 + 0.5f, eyeY, 8 + 0.5f, pitch: 0f, yaw: 0f));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(feetY, player.PositionY, precision: 3);

        new FloorDropSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(64, player.Inventory.Get(6).Count);
        Assert.True(fx.World.FloorDrops.TryTake(8, Blocks.FlatGrassY, 8, out _, out var left, out _));
        Assert.Equal(1, left);
    }


    [Fact]
    public void FloorDrop_default_delay_blocks_same_tick_pickup()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("delayblock");
        StandNear(player, 5, 64, 5);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 1));

        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            5, 64, 5, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 50, out _)); // default delay 10

        new FloorDropSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(1, fx.World.FloorDrops.Count);
    }


    [Fact]
    public void FloorDrop_after_10_ticks_partial_pickup()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("delayok");
        StandNear(player, 5, 64, 5);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Dirt, PlayerInventory.MaxStack));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 62));

        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            5, 64, 5, StackId.FromBlock(Blocks.Dirt), 5, entityRuntimeIdIfNew: 51, out _));

        var sys = new FloorDropSystem(fx.World);
        for (var t = 0; t < FloorDropStore.DefaultPickupDelay; t++)
            sys.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(64, player.Inventory.Get(0).Count);
        Assert.True(fx.World.FloorDrops.TryTake(5, 64, 5, out _, out var left, out _));
        Assert.Equal(3, left);
    }


    [Fact]
    public void BlockSystem_rejects_survival_break_without_start_break()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("nostart");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(0, 90, 0));
    }


    [Fact]
    public void BlockSystem_break_accepts_on_last_mining_tick()
    {
        // Client predict often arrives at elapsed == need-1; gate must not wait for need.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("lasttick");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var need = Blocks.BreakTicks(Blocks.Dirt);
        Assert.True(need > 1);

        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need - 1);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
    }


    [Fact]
    public void BlockSystem_break_rejects_two_ticks_early()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("tooearly");
        var viewer = fx.AddInGamePlayer("viewer");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var need = Blocks.BreakTicks(Blocks.Dirt);
        Assert.True(need > 2);

        Assert.True(player.Dig.SubmitDigStart(0, 90, 0, fx.Clock.CurrentTick, need));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Dig.HasBreakTarget);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        fx.Clock.AdvanceBy(need - 2);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(0, 90, 0));
        Assert.True(fx.Transport.Captured.Count >= 1,
            "early DigAuthorized reject must StopCrack (not only Resync)");
        _ = viewer;
    }


    [Fact]
    public void BlockSystem_same_tick_start_and_predict_rejects_when_need_gt_1()
    {
        // Real AuthInput same-tick: DigStartedTick == CurrentTick → elapsed 0 < need-1.
        // Must reject without leaving a zombie crack (CancelPending skips ApplyDig StartCrack).
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sametick");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var need = Blocks.BreakTicks(Blocks.Dirt);
        Assert.True(need > 1);

        var start = fx.Clock.CurrentTick;
        Assert.True(player.Dig.SubmitDigStart(0, 90, 0, start, need));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, start, need)));
        player.Dig.CancelPendingDigStart(0, 90, 0);

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(0, 90, 0));
        Assert.False(player.Dig.HasBreakTarget);
    }


    [Fact]
    public void BlockSystem_dig_authorized_break_after_elapsed()
    {
        // DigAuthorized break after enough ticks; BlockEdit consumes the completion on tick.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fastbreak");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var need = Blocks.BreakTicks(Blocks.Dirt);
        var start = fx.Clock.CurrentTick;
        fx.Clock.AdvanceBy(need);

        Assert.True(player.Dig.SubmitDigStart(0, 90, 0, start, need));
        Assert.True(player.Dig.TryGetDigAuth(0, 90, 0, out var authStart, out var authNeed));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, authStart, authNeed)));
        player.Dig.CancelPendingDigStart(0, 90, 0);

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.False(player.Dig.HasBreakTarget);
    }


    [Fact]
    public void BlockSystem_break_survives_dig_retarget_before_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chainbreak");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        fx.World.SetBlock(1, 90, 0, Blocks.Dirt);

        var need = Blocks.BreakTicks(Blocks.Dirt);
        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)));

        // Simulate a tick-owned retarget before BlockEdit drains the queued completion (§27).
        player.Dig.BeginBreak(1, 90, 0, fx.Clock.CurrentTick, need);
        Assert.True(player.Dig.IsBreakTarget(1, 90, 0));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(1, 90, 0));
    }


    [Fact]
    public void BlockSystem_abort_then_stale_predict_rejects_then_redig_breaks()
    {
        // Mirrors cancel+redig: Abort clears dig; Predict without DigAuthorized rejects;
        // Start + DigAuthorized on the next attempt succeeds (§27 AuthInput order).
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("cancelredig");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);

        var need = Blocks.BreakTicks(Blocks.Dirt);
        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need / 2);
        player.Dig.AbortBreak();
        Assert.False(player.Dig.HasBreakTarget);

        // Stale Predict after Abort (no dig auth) — first "retry" that used to feel broken.
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(0, 90, 0));

        // Proper redig: Start then DigAuthorized Predict.
        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
    }


    [Fact]
    public void BlockSystem_dig_start_intent_applies_BeginBreak_on_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("digger");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var need = Blocks.BreakTicks(Blocks.Dirt);
        var start = fx.Clock.CurrentTick;

        Assert.True(player.Dig.SubmitDigStart(0, 90, 0, start, need));
        Assert.False(player.Dig.HasBreakTarget);
        Assert.True(player.Dig.TryGetDigAuth(0, 90, 0, out var authStart, out var authNeed));
        Assert.Equal(start, authStart);
        Assert.Equal(need, authNeed);

        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Dig.IsBreakTarget(0, 90, 0));
        Assert.Equal(need, player.Dig.BreakRequiredTicks);
    }


    [Fact]
    public void BlockSystem_dig_abort_intent_suppresses_auth_before_tick_without_mutating_target()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("abort");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var need = Blocks.BreakTicks(Blocks.Dirt);
        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);

        Assert.True(player.Dig.SubmitDigAbort(0, 90, 0));
        Assert.True(player.Dig.HasBreakTarget, "only the tick may clear the authoritative break target");
        Assert.False(player.Dig.TryGetDigAuth(0, 90, 0, out _, out _));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.Dig.HasBreakTarget);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(0, 90, 0));
    }


    [Fact]
    public void BlockSystem_same_cell_two_digs_first_writer_wins_loot()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        var b = fx.AddInGamePlayer("bob");
        StandNear(a, 0, 90, 0);
        StandNear(b, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);

        var need = Blocks.BreakTicks(Blocks.Dirt);
        var start = fx.Clock.CurrentTick;
        fx.Clock.AdvanceBy(need);

        Assert.True(a.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, start, need)));
        Assert.True(b.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, start, need)));

        var beforeA = CountRuntime(a.Inventory, Blocks.Dirt);
        var beforeB = CountRuntime(b.Inventory, Blocks.Dirt);

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        var gainedA = CountRuntime(a.Inventory, Blocks.Dirt) - beforeA;
        var gainedB = CountRuntime(b.Inventory, Blocks.Dirt) - beforeB;
        Assert.Equal(1, gainedA + gainedB);
        Assert.True(gainedA == 1 ^ gainedB == 1);
    }


    [Fact]
    public void BlockSystem_idle_dig_aborts_and_stops_crack()
    {
        var fx = new IntentTestFixture();
        var miner = fx.AddInGamePlayer("miner");
        var viewer = fx.AddInGamePlayer("viewer");
        StandNear(miner, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        var need = Blocks.BreakTicks(Blocks.Stone);
        Assert.True(need > (int)Player.PlayerDig.DigIdleAbortTicks);

        Assert.True(miner.Dig.SubmitDigStart(0, 90, 0, fx.Clock.CurrentTick, need));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(miner.Dig.HasBreakTarget);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        // Idle grace applies only after dig window — advance past need + DigIdleAbortTicks.
        fx.Clock.AdvanceBy(need + (int)Player.PlayerDig.DigIdleAbortTicks);
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(miner.Dig.HasBreakTarget);
        Assert.True(fx.Transport.Captured.Count >= 1, "idle dig should StopCrack to peers");
        _ = viewer;
    }


    [Fact]
    public void BlockSystem_dig_survives_idle_grace_until_BreakRequiredTicks()
    {
        // Regression: stone-by-hand need (~150) > DigIdleAbortTicks (40). Without MarkDigActive
        // the old idle abort StopCrack'd mid-swing; client AbortBreak cleared provisional auth
        // → Predict Resync + no loot (§74 still means no cobble without pickaxe).
        var fx = new IntentTestFixture();
        var miner = fx.AddInGamePlayer("miner");
        StandNear(miner, 0, 90, 0);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(miner.Inventory.TrySetBlock(i, Blocks.Air, 0));

        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        var need = Blocks.BreakTicks(Blocks.Stone);
        Assert.True(need > (int)Player.PlayerDig.DigIdleAbortTicks);

        Assert.True(miner.Dig.SubmitDigStart(0, 90, 0, fx.Clock.CurrentTick, need));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(miner.Dig.HasBreakTarget);

        fx.Clock.AdvanceBy((int)Player.PlayerDig.DigIdleAbortTicks);
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(miner.Dig.HasBreakTarget, "must not idle-abort before BreakRequiredTicks");

        fx.Clock.AdvanceBy(need - 1 - (int)Player.PlayerDig.DigIdleAbortTicks);
        Assert.True(miner.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, miner.Dig.BreakStartedTick, miner.Dig.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(0, fx.World.FloorDrops.Count);
        Assert.Equal(0, CountRuntime(miner.Inventory, Blocks.Stone));
        Assert.Equal(0, CountRuntime(miner.Inventory, Blocks.Cobblestone));
    }


    [Fact]
    public void BlockSystem_fans_UpdateBlock_to_joiner_who_Knows_column()
    {
        var fx = new IntentTestFixture();
        var placer = fx.AddInGamePlayer("placer");
        var joiner = fx.AddInGamePlayer("joiner");
        joiner.IsInGame = false;
        joiner.Chunks.RememberMany([(0, 0)]);
        StandForPlace(placer, 2, 64, 2);
        Assert.True(placer.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        Assert.True(placer.Intents.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 2, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(2, 64, 2));
        Assert.True(fx.Transport.Captured.Count >= 1,
            "joiner with Knows(0,0) must receive UpdateBlock while !IsInGame");
    }


    [Fact]
    public void BlockSystem_rejects_place_from_storage_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("deeplace");
        StandForPlace(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Stone, 5));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Stone, hotbarSlot: 9)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.NotEqual(Blocks.Stone, fx.World.GetBlock(1, 64, 1));
        Assert.Equal(5, player.Inventory.Get(9).Count);
    }


    [Fact]
    public void BlockSystem_rejects_edit_beyond_reach()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("far");
        player.PositionX = 0;
        player.PositionY = 64;
        player.PositionZ = 0;
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        // ~20 blocks away horizontally — outside MaxBlockReach
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(20, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.NotEqual(Blocks.Stone, fx.World.GetBlock(20, 64, 0));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_IsWithinReach_uses_eye_height()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("eyes");
        player.PositionX = 0.5f;
        player.PositionY = 64f;
        player.PositionZ = 0.5f;
        // Block at feet Y is within reach via eye offset
        Assert.True(BlockEditSystem.IsWithinReach(player, 0, 64, 0));
        Assert.False(BlockEditSystem.IsWithinReach(player, 0, 64 + 20, 0));
    }


    [Fact]
    public void SetBlock_during_BlockSystem_tick_updates_ram_without_blocking()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fast");
        StandForPlace(player, 9, 64, 9);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 3));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(9, 64, 9, Blocks.Stone, hotbarSlot: 0)));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(9, 64, 9));
    }


    [Fact]
    public void BlockSystem_place_chest_ensures_store()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chestor");
        StandForPlace(player, 4, 64, 4);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Chest, 2));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(4, 64, 4, Blocks.Chest, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Chest, fx.World.GetBlock(4, 64, 4));
        Assert.True(fx.World.Chests.TryGetSlots(4, 64, 4, out _));
    }


    [Fact]
    public void BlockSystem_break_oriented_chest_drops_item_form()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chestbreak");
        StandNear(player, 5, 64, 5);
        var north = Blocks.ChestForFacing(Blocks.CardinalNorth);
        Assert.NotEqual(Blocks.Chest, north);
        fx.World.SetBlock(5, 64, 5, north);
        Assert.True(fx.World.Chests.TryEnsure(5, 64, 5));

        // Starter kit already has chests in hotbar slot 5 — merge target.
        var before = player.Inventory.Get(5).Count;

        QueueReadyBreak(fx.Clock, fx.World, player, 5, 64, 5);
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(5, 64, 5));
        Assert.False(fx.World.Chests.TryGetSlots(5, 64, 5, out _));
        Assert.Equal(Blocks.Chest, player.Inventory.Get(5).Id.Value);
        Assert.Equal(before + 1, player.Inventory.Get(5).Count);
    }


    [Fact]
    public void BlockEditSystem_cancels_pending_world_mutation_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-edit");
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Dirt)));

        player.IsInGame = false;
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(1, 64, 1));
    }


    [Fact]
    public void BlockDigSystem_cancels_pending_dig_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-dig");
        Assert.True(player.Dig.SubmitDigStart(1, 64, 1, fx.Clock.CurrentTick, requiredTicks: 20));

        player.IsInGame = false;
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Dig.HasBreakTarget);
    }


    [Fact]
    public void BlockSystem_creative_place_does_not_consume_hotbar()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("cplace", GameMode.Creative);
        StandForPlace(player, 2, 64, 2);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 2, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(2, 64, 2));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void BlockSystem_creative_break_instant_without_loot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("cbreak", GameMode.Creative);
        StandNear(player, 3, 64, 3);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        fx.World.SetBlock(3, 64, 3, Blocks.Stone);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 3, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(3, 64, 3));
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }
}
