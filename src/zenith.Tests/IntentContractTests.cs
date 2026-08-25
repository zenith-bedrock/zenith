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
using Zenith.Raknet.Log;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Tests;

// Shared test harness (IntentTestFixture, RecordingRakNetServer, SilentLogger, StubSessionHandler)
// lives in IntentTestFixture.cs — this file is one of its ~30 consumers, not its home.

public class IntentContractTests
{
    private static void StandNear(Player.Player player, int x, int y, int z)
    {
        player.PositionX = x + 0.5f;
        player.PositionY = y; // feet; eyes = Y + EyeHeight still within reach of block center
        player.PositionZ = z + 0.5f;
    }

    /// <summary>
    /// Feet in the +X adjacent cell so place into (x,y,z) is not self-obstructed (§15).
    /// </summary>
    private static void StandForPlace(Player.Player player, int x, int y, int z)
    {
        player.PositionX = x + 1.5f;
        player.PositionY = y;
        player.PositionZ = z + 0.5f;
    }

    private static void BeginBreakReady(GameClock clock, World.World world, Player.Player player, int x, int y, int z)
    {
        var held = player.Inventory.Get(player.SelectedHotbarSlot);
        var heldId = held.IsEmpty ? default : held.Id;
        var need = Blocks.BreakTicks(world.GetBlock(x, y, z), heldId);
        player.BeginBreak(x, y, z, clock.CurrentTick, need, heldId);
        if (need > 0)
            clock.AdvanceBy(need);
    }

    /// <summary>Queue a Survival break with dig auth frozen into the intent (§27).</summary>
    private static void QueueReadyBreak(
        GameClock clock,
        World.World world,
        Player.Player player,
        int x,
        int y,
        int z)
    {
        BeginBreakReady(clock, world, player, x, y, z);
        var need = player.BreakRequiredTicks;
        var intent = need > 0
            ? BlockEditIntent.BreakWithDig(x, y, z, player.BreakStartedTick, player.BreakRequiredTicks)
            : BlockEditIntent.Set(x, y, z, Blocks.Air);
        Assert.True(player.SubmitBlockEdit(intent));
    }

    private static void EquipWoodenPickaxe(Player.Player player, int slot = 0)
    {
        Tools.EnsureLoaded();
        Assert.True(player.Inventory.TrySetItem(slot, Tools.Require("minecraft:wooden_pickaxe"), 1));
        player.SelectedHotbarSlot = slot;
    }

    private static void SubmitCraftRecipeRequest(Player.Player player, int requestId, uint recipeNetId)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftRecipe);
        writer.WriteUnsignedVarInt(checked((int)recipeNetId));
        writer.WriteByte(1); // craft times
        writer.WriteUnsignedVarInt(0); // filter strings
        writer.WriteInt(0, BinaryStream.Endianess.Little); // filter cause

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref stream);
        Assert.True(handled);
    }

    private static void SubmitDropRequest(Player.Player player, int requestId, byte containerId, byte slot)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionDrop);
        writer.WriteByte(1); // count
        WriteSlotInfo(ref writer, containerId, slot);
        writer.WriteBool(false); // randomly
        SubmitItemStackRequest(player, writer);
    }

    private static void SubmitChestAndCraftRequest(Player.Player player, int requestId, uint recipeNetId)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(2); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(1); // count
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);
        WriteSlotInfo(ref writer, InventoryContainerMap.Chest, 0);
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftRecipe);
        writer.WriteUnsignedVarInt(checked((int)recipeNetId));
        writer.WriteByte(1); // craft times
        SubmitItemStackRequest(player, writer);
    }

    private static void SubmitChestTransferRequest(Player.Player player, int requestId)
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(requestId);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(1); // count
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);
        WriteSlotInfo(ref writer, InventoryContainerMap.Chest, 0);
        SubmitItemStackRequest(player, writer);
    }

    private static void WriteSlotInfo(ref BinaryStream writer, byte containerId, byte slot)
    {
        writer.WriteByte(containerId);
        writer.WriteBool(false); // dynamic container id
        writer.WriteByte(slot);
        writer.WriteInt(0, BinaryStream.Endianess.Little); // stack network id
    }

    private static void WriteDynamicSlotInfo(ref BinaryStream writer, byte containerId, byte slot, uint dynamicId)
    {
        writer.WriteByte(containerId);
        writer.WriteBool(true);
        writer.WriteUInt(dynamicId, BinaryStream.Endianess.Little);
        writer.WriteByte(slot);
        writer.WriteInt(0, BinaryStream.Endianess.Little);
    }

    private static void WriteStackActionHeader(ref BinaryStream writer, byte legacyActionId)
    {
        writer.WriteUnsignedVarInt(legacyActionId < ItemStackRequestPacket.ActionPlaceInContainer
            ? legacyActionId
            : legacyActionId - 2);
        writer.WriteByte(legacyActionId);
    }

    private static void SubmitItemStackRequest(Player.Player player, BinaryStream writer)
    {
        writer.WriteUnsignedVarInt(0); // filter strings
        writer.WriteInt(0, BinaryStream.Endianess.Little); // filter cause

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref stream);
        Assert.True(handled);
    }

    [Fact]
    public void MovementSystem_void_triggers_death_and_dumps_survival_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));

        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(0f, player.Health);
        Assert.Equal("generic", player.DeathCause);
        // Still at void pose until client Respawn — no soft-rescue teleport.
        Assert.Equal(3.5f, player.PositionX);
        Assert.Equal(MovementSystem.VoidRescueY - 1f, player.PositionY);
        Assert.Equal(4.5f, player.PositionZ);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.True(fx.World.FloorDrops.Count >= 1);
    }

    [Fact]
    public void MovementSystem_repeated_dead_ticks_do_not_duplicate_death_loot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));
        var movement = new MovementSystem(fx.Players);

        movement.Tick(fx.Clock, fx.Players.Online);
        var stone = StackId.FromBlock(Blocks.Stone);
        var initialLoot = fx.World.FloorDrops.Snapshot()
            .Where(drop => drop.Id == stone)
            .Sum(drop => drop.Count);

        movement.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(5, initialLoot);
        Assert.Equal(initialLoot, fx.World.FloorDrops.Snapshot()
            .Where(drop => drop.Id == stone)
            .Sum(drop => drop.Count));
    }

    [Fact]
    public void MovementSystem_respawn_restores_spawn_with_empty_bag_after_death_loot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 7));
        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));
        var movement = new MovementSystem(fx.Players);
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsDead);
        Assert.True(player.Inventory.Get(0).IsEmpty);

        player.SubmitRespawn();
        movement.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsDead);
        Assert.Equal(20f, player.Health);
        Assert.Equal(0f, player.PositionX);
        Assert.Equal(Blocks.FlatSpawnY, player.PositionY);
        Assert.Equal(0f, player.PositionZ);
        Assert.Equal(0f, player.Pitch);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.True(fx.World.FloorDrops.Count >= 1);
    }

    [Fact]
    public void MovementSystem_void_death_creative_keeps_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creativefall", GameMode.Creative);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));

        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void MovementSystem_void_death_keeps_slot_when_floor_softcap_full()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("capfall");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 3));

        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(
                i, 64, 0, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: i + 1, out _));

        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsDead);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(0).Id.Value);
        Assert.Equal(3, player.Inventory.Get(0).Count);
        Assert.Equal(FloorDropStore.SoftCap, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void MovementSystem_sneak_fans_SetActorData_flags_to_peer()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        var b = fx.AddInGamePlayer("bob");
        StandNear(a, 0, 64, 0);
        StandNear(b, 2, 64, 0);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        a.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            pitch: 0f,
            yaw: 0f,
            sneaking: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(a.IsSneaking);
        Assert.True(a.LastReplicatedSneaking);
        Assert.False(a.IsSprinting);
        Assert.True(fx.Transport.Captured.Count >= 1, "peer must receive SetActorData FLAGS");
        _ = b;
    }

    [Fact]
    public void MovementSystem_sprint_start_clears_sneak()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("sprinter");
        var b = fx.AddInGamePlayer("viewer");
        a.IsSneaking = true;
        a.LastReplicatedSneaking = true;

        a.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            pitch: 0f,
            yaw: 0f,
            sneaking: false,
            sprintStart: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(a.IsSprinting);
        Assert.False(a.IsSneaking);
        Assert.True(a.LastReplicatedSprinting);
        Assert.False(a.LastReplicatedSneaking);
        _ = b;
    }

    [Fact]
    public void MovementSystem_missed_swing_fans_Animate_to_peer()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("puncher");
        var b = fx.AddInGamePlayer("viewer");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        // Same pose so Absolute is not dirty — only swing fan-out.
        a.LastReplicatedX = a.PositionX;
        a.LastReplicatedY = a.PositionY;
        a.LastReplicatedZ = a.PositionZ;
        a.LastReplicatedPitch = a.Pitch;
        a.LastReplicatedYaw = a.Yaw;
        a.LastReplicatedHeadYaw = a.HeadYaw;

        a.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            pitch: a.Pitch,
            yaw: a.Yaw,
            missedSwing: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1, "peer must receive Animate SwingArm");
        _ = b;
    }

    [Fact]
    public void MovementSystem_respawn_clears_sneak_sprint()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("croucher");
        player.IsSneaking = true;
        player.IsSprinting = true;
        player.SubmitMovementInput(MovementInputState.From(
            x: 0f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 0f,
            pitch: 0f,
            yaw: 0f));
        var movement = new MovementSystem(fx.Players);
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsDead);
        Assert.False(player.IsSneaking);
        Assert.False(player.IsSprinting);

        player.SubmitRespawn();
        movement.Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.IsDead);
        Assert.False(player.IsSneaking);
        Assert.False(player.IsSprinting);
        Assert.False(player.LastReplicatedSneaking);
        Assert.False(player.LastReplicatedSprinting);
    }

    [Fact]
    public void PlayerVisibility_RelayEmote_sends_to_other_InGame_peers()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("emoter");
        var b = fx.AddInGamePlayer("audience");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerVisibility.RelayEmote(
            a,
            emoteId: "4c8cc107-7341-4d95-91eb-5386cc311b25",
            tickLength: 40,
            xuid: "",
            platformChatId: "",
            fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1);
        _ = b;
    }

    [Fact]
    public void PlayerVisibility_RelaySwingArm_sends_to_other_InGame_peers()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("miner");
        var b = fx.AddInGamePlayer("viewer");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerVisibility.RelaySwingArm(a, fx.Players.Online, swingSource: "mine");
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1);
        _ = b;
    }

    [Fact]
    public void BlockSystem_drains_fifo_queue_in_one_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("builder");
        // Stand on top of the middle cell (Y+1): in reach of 1–3, no body ∩ place cells at Y=64.
        StandNear(player, 2, 65, 0);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 10));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 0, Blocks.Stone, hotbarSlot: 0)));

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.TryConsumeBlockEdit(out _));
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

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, Blocks.GrassBlock, hotbarSlot: 2)));

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(
            5, 70, 6, Blocks.Stone, hotbarSlot: 0, expectedPlacementStackId: StackId.FromBlock(Blocks.Stone))));

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(6, 64, 6, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(7, 64, 7, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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

        Assert.True(b.SubmitBlockEdit(BlockEditIntent.Set(10, 64, 10, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(11, 64, 11, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(11, 64, 11));
    }

    [Fact]
    public void BlockSystem_rejects_place_when_HotbarSlot_invalid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("badslot");
        StandForPlace(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Stone, hotbarSlot: -1)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(4, 64, 4, goldRid, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(4, 80, 4, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 100, 0, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        fx.World.Chests.Ensure(5, 64, 5);
        Assert.True(fx.World.Chests.TrySet(5, 64, 5, 0, InventorySlot.OfBlock(Blocks.Dirt, 7)));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(5, 64, 5, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        player.SubmitMovementInput(MovementInputState.FromClientAuthInput(
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
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
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

        player.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need - 1);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.BreakStartedTick, player.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
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

        Assert.True(player.SubmitDigStart(0, 90, 0, fx.Clock.CurrentTick, need));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.HasBreakTarget);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        fx.Clock.AdvanceBy(need - 2);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.BreakStartedTick, player.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
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
        Assert.True(player.SubmitDigStart(0, 90, 0, start, need));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, start, need)));
        player.CancelPendingDigStart(0, 90, 0);

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(0, 90, 0));
        Assert.False(player.HasBreakTarget);
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

        Assert.True(player.SubmitDigStart(0, 90, 0, start, need));
        Assert.True(player.TryGetDigAuth(0, 90, 0, out var authStart, out var authNeed));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, authStart, authNeed)));
        player.CancelPendingDigStart(0, 90, 0);

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.False(player.HasBreakTarget);
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
        player.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.BreakStartedTick, player.BreakRequiredTicks)));

        // Simulate a tick-owned retarget before BlockEdit drains the queued completion (§27).
        player.BeginBreak(1, 90, 0, fx.Clock.CurrentTick, need);
        Assert.True(player.IsBreakTarget(1, 90, 0));

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        player.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need / 2);
        player.AbortBreak();
        Assert.False(player.HasBreakTarget);

        // Stale Predict after Abort (no dig auth) — first "retry" that used to feel broken.
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(0, 90, 0));

        // Proper redig: Start then DigAuthorized Predict.
        player.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.BreakStartedTick, player.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
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

        Assert.True(player.SubmitDigStart(0, 90, 0, start, need));
        Assert.False(player.HasBreakTarget);
        Assert.True(player.TryGetDigAuth(0, 90, 0, out var authStart, out var authNeed));
        Assert.Equal(start, authStart);
        Assert.Equal(need, authNeed);

        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.IsBreakTarget(0, 90, 0));
        Assert.Equal(need, player.BreakRequiredTicks);
    }

    [Fact]
    public void BlockSystem_dig_abort_intent_suppresses_auth_before_tick_without_mutating_target()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("abort");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var need = Blocks.BreakTicks(Blocks.Dirt);
        player.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);

        Assert.True(player.SubmitDigAbort(0, 90, 0));
        Assert.True(player.HasBreakTarget, "only the tick may clear the authoritative break target");
        Assert.False(player.TryGetDigAuth(0, 90, 0, out _, out _));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.HasBreakTarget);
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
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

        Assert.True(a.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, start, need)));
        Assert.True(b.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, start, need)));

        var beforeA = CountRuntime(a.Inventory, Blocks.Dirt);
        var beforeB = CountRuntime(b.Inventory, Blocks.Dirt);

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        Assert.True(need > (int)Player.Player.DigIdleAbortTicks);

        Assert.True(miner.SubmitDigStart(0, 90, 0, fx.Clock.CurrentTick, need));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(miner.HasBreakTarget);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        // Idle grace applies only after dig window — advance past need + DigIdleAbortTicks.
        fx.Clock.AdvanceBy(need + (int)Player.Player.DigIdleAbortTicks);
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(miner.HasBreakTarget);
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
        Assert.True(need > (int)Player.Player.DigIdleAbortTicks);

        Assert.True(miner.SubmitDigStart(0, 90, 0, fx.Clock.CurrentTick, need));
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(miner.HasBreakTarget);

        fx.Clock.AdvanceBy((int)Player.Player.DigIdleAbortTicks);
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(miner.HasBreakTarget, "must not idle-abort before BreakRequiredTicks");

        fx.Clock.AdvanceBy(need - 1 - (int)Player.Player.DigIdleAbortTicks);
        Assert.True(miner.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, miner.BreakStartedTick, miner.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(0, fx.World.FloorDrops.Count);
        Assert.Equal(0, CountRuntime(miner.Inventory, Blocks.Stone));
        Assert.Equal(0, CountRuntime(miner.Inventory, Blocks.Cobblestone));
    }

    [Fact]
    public void MovementSystem_on_ground_change_dirties_Absolute_with_flag()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        var b = fx.AddInGamePlayer("bob");
        a.PositionX = 1f;
        a.PositionY = Blocks.FlatSpawnY;
        a.PositionZ = 2f;
        a.IsOnGround = false;
        a.LastReplicatedX = a.PositionX;
        a.LastReplicatedY = a.PositionY;
        a.LastReplicatedZ = a.PositionZ;
        a.LastReplicatedPitch = a.Pitch;
        a.LastReplicatedYaw = a.Yaw;
        a.LastReplicatedHeadYaw = a.HeadYaw;
        a.LastReplicatedOnGround = false;

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        // Same XYZ/look — only on-ground flips.
        a.SubmitMovementInput(MovementInputState.FromClientAuthInput(
            a.PositionX,
            a.PositionY + Blocks.PlayerEyeHeight,
            a.PositionZ,
            a.Pitch,
            a.Yaw,
            onGround: true));
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(a.IsOnGround);
        Assert.True(a.LastReplicatedOnGround);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "on-ground-only change must fan Absolute so peers get FLAG_ON_GROUND");
        _ = b;
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

        Assert.True(placer.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 2, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(2, 64, 2));
        Assert.True(fx.Transport.Captured.Count >= 1,
            "joiner with Knows(0,0) must receive UpdateBlock while !IsInGame");
    }

    [Fact]
    public void ChunkStreamSystem_overlay_resync_on_NeedsOverlayResync()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("resync");
        player.Chunks.Radius = 1;
        player.Chunks.RememberMany([(0, 0)]);
        fx.World.SetBlock(3, 64, 3, Blocks.OakPlanks);
        player.Chunks.NeedsOverlayResync = true;

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(player.Chunks.NeedsOverlayResync);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "NeedsOverlayResync must emit UpdateBlock for known-column overlays");
    }

    [Fact]
    public void ChunkStreamSystem_floor_drop_catchup_on_NeedsOverlayResync()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropresync");
        player.Chunks.Radius = 1;
        player.Chunks.RememberMany([(0, 0)]);
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            3, 64, 3, StackId.FromBlock(Blocks.Dirt), 2, entityRuntimeIdIfNew: 77, out _));
        player.Chunks.NeedsOverlayResync = true;

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(player.Chunks.NeedsOverlayResync);
        Assert.Equal(1, fx.World.FloorDrops.Count);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "NeedsOverlayResync must emit AddItemActor for floor drops in known columns");
    }

    [Fact]
    public void ChunkStreamSystem_streams_while_IsSpawning_without_IsInGame()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("spawning");
        player.IsInGame = false;
        player.IsSpawning = true;
        player.Chunks.Radius = 0;
        player.PositionX = 0.5f;
        player.PositionY = 64f;
        player.PositionZ = 0.5f;
        _ = player.Chunks.PublisherCenterChanged(0, 0);

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Chunks.Knows(0, 0),
            "IsSpawning must allow ChunkStream to begin columns before IsInGame (ADR §70)");
    }

    [Fact]
    public void ChunkStreamSystem_skips_when_neither_in_game_nor_spawning()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("waiting");
        player.IsInGame = false;
        player.IsSpawning = false;
        player.Chunks.Radius = 0;
        player.PositionX = 0.5f;
        player.PositionZ = 0.5f;
        _ = player.Chunks.PublisherCenterChanged(0, 0);

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Chunks.Knows(0, 0));
    }

    [Fact]
    public async Task ChunkStreamSystem_emits_completed_stream_only_when_tick_drains_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stream-result");
        player.Chunks.Radius = -1; // This test controls the only stream slot explicitly.
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        Assert.True(player.Chunks.TryBegin(0, 0, out var epoch));

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.Chunks.CompleteStream(0, 0, epoch, column);
        FlushRaknet(fx.Players);
        Assert.Empty(fx.Transport.Captured);

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                FlushRaknet(fx.Players);
                return fx.Transport.Captured.Count > 0;
            },
            TimeSpan.FromSeconds(5)));
        FlushRaknet(fx.Players);
        Assert.NotEmpty(fx.Transport.Captured);
    }

    [Fact]
    public async Task ChunkStreamSystem_abandons_completed_stream_when_player_leaves_before_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stream-leave");
        player.Chunks.Radius = -1;
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        Assert.True(player.Chunks.TryBegin(0, 0, out var epoch));
        player.Chunks.CompleteStream(0, 0, epoch, column);
        player.IsInGame = false;
        player.IsSpawning = false;

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Chunks.Knows(0, 0));
    }

    /// <summary>
    /// Phase XXVI post-impl audit: this used to drive the legacy bulk `CompletePreSpawn` shortcut,
    /// which is dead in production since `StartPendingPreSpawn` always goes through the streaming
    /// `PreSpawnLoad` path now. Rewritten to submit a real pre-spawn request and prove publication
    /// still only happens through `Tick` (via `PumpStreamingPreSpawn`), not synchronously at submit.
    /// </summary>
    [Fact]
    public void ChunkStreamSystem_streaming_pre_spawn_publishes_only_through_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("pre-spawn", isInGame: false);
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius: 0));

        Assert.False(player.IsSpawning);
        Assert.False(player.Chunks.Knows(0, 0));
        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        var system = new ChunkStreamSystem(fx.World);
        Assert.True(SpinWait.SpinUntil(() =>
        {
            FlushRaknet(fx.Players);
            system.Tick(fx.Clock, fx.Players.Online);
            return player.IsSpawning;
        }, TimeSpan.FromSeconds(2)));

        Assert.True(player.Chunks.Knows(0, 0));
        FlushRaknet(fx.Players);
        Assert.NotEmpty(fx.Transport.Captured);
    }

    /// <summary>
    /// Phase XXVI post-impl audit: the original version of this test also drove the dead bulk
    /// `CompletePreSpawn` path, so it never actually exercised `PumpStreamingPreSpawn`'s own
    /// per-tick publish budget — the entire point of ADR §111's "never stall a tick" guarantee.
    /// Rewritten to submit a real, oversized pre-spawn request and assert the known-column count
    /// never grows by more than the configured per-tick budget in any single tick.
    /// </summary>
    [Fact]
    public void ChunkStreamSystem_bounds_streaming_pre_spawn_publication_per_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("large-pre-spawn", isInGame: false);
        const int viewRadius = 3; // 49 columns, comfortably above the per-tick budget
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius));

        var system = new ChunkStreamSystem(fx.World);
        var fullViewCount = (viewRadius * 2 + 1) * (viewRadius * 2 + 1);
        var budget = Math.Min(
            ChunkStreamSystem.MaxPreSpawnColumnsPerTick,
            fx.Context.Config.World.PreSpawnColumnsPerTick);

        var known = new List<(int X, int Z)>();
        var previousCount = 0;
        var sawAnyPublish = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (previousCount < fullViewCount && DateTime.UtcNow < deadline)
        {
            FlushRaknet(fx.Players);
            system.Tick(fx.Clock, fx.Players.Online);
            player.Chunks.CopyKnown(known);
            var delta = known.Count - previousCount;
            Assert.True(delta <= budget, $"tick published {delta} columns, budget is {budget}");
            if (delta > 0) sawAnyPublish = true;
            previousCount = known.Count;
            if (delta == 0) Thread.Yield();
        }

        Assert.True(sawAnyPublish, "expected at least one tick to publish columns within the budget");
        Assert.Equal(fullViewCount, previousCount);
    }

    /// <summary>
    /// ADR §114 — residency for the whole pre-spawn square must be acquired synchronously in the
    /// same tick the request is consumed, before StreamRadiusAsync's hydration even starts.
    /// Otherwise a same-tick eviction sweep could evict a chunk mid-join (hydrated, zero viewers)
    /// before RememberMany ever gets a chance to acquire it later, column by column.
    /// </summary>
    [Fact]
    public void ChunkStreamSystem_acquires_residency_for_the_whole_pre_spawn_square_before_streaming_starts()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("pre-spawn-residency", isInGame: false);
        const int viewRadius = 2;
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius));

        var system = new ChunkStreamSystem(fx.World);
        FlushRaknet(fx.Players);
        system.Tick(fx.Clock, fx.Players.Online); // consumes the pre-spawn request this tick

        for (var x = -viewRadius; x <= viewRadius; x++)
        for (var z = -viewRadius; z <= viewRadius; z++)
            Assert.True(fx.World.ChunkResidency.HasViewers(x, z), $"chunk ({x},{z}) should have a viewer");
    }

    [Fact]
    public void ChunkStreamSystem_releases_spawn_after_ready_radius_before_full_view_is_loaded()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("streaming-pre-spawn", isInGame: false);
        const int viewRadius = 4;
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius));

        var system = new ChunkStreamSystem(fx.World);
        var fullViewCount = (viewRadius * 2 + 1) * (viewRadius * 2 + 1);
        var known = new List<(int X, int Z)>();
        var readyObserved = SpinWait.SpinUntil(() =>
        {
            FlushRaknet(fx.Players);
            system.Tick(fx.Clock, fx.Players.Online);
            player.Chunks.CopyKnown(known);
            return player.IsSpawning;
        }, TimeSpan.FromSeconds(10));

        Assert.True(readyObserved, "spawn must be released after the central ready radius is transmitted");
        player.Chunks.CopyKnown(known);
        Assert.InRange(known.Count, 1, fullViewCount - 1);
        Assert.False(player.IsInGame);
    }

    [Fact]
    public void ChunkStreamSystem_applies_spawn_ready_only_while_spawn_is_current()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("spawn-ready", isInGame: false);
        player.IsSpawning = true;
        player.SubmitSpawnReady();

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsInGame);
        Assert.False(player.IsSpawning);
    }

    [Fact]
    public void ChunkStreamSystem_discards_late_spawn_ready_after_spawn_is_no_longer_current()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("stale-spawn-ready", isInGame: false);
        player.SubmitSpawnReady();

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsInGame);
        Assert.False(player.IsSpawning);
        Assert.False(player.TryConsumeSpawnReady());
    }

    private static int CountRuntime(PlayerInventory inv, int runtimeId)
    {
        var n = 0;
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
        {
            var s = inv.Get(i);
            if (s.Id.IsBlock && s.Id.Value == runtimeId)
                n += s.Count;
        }
        return n;
    }

    [Fact]
    public void BlockSystem_rejects_place_from_storage_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("deeplace");
        StandForPlace(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Stone, 5));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Stone, hotbarSlot: 9)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(20, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
    public void MobEquipment_decode_reads_hotbar_slot()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarLong(2); // actor runtime
        // empty-ish network item
        writer.WriteShort(1, BinaryStream.Endianess.Little);
        writer.WriteUShort(1, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0);
        writer.WriteBool(true);
        writer.WriteVarInt(47); // stack network id
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(0);
        writer.WriteByte(0); // inventory slot
        writer.WriteByte(3); // hotbar
        writer.WriteByte(0); // window
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        var packet = new MobEquipmentPacket();
        packet.Decode(ref stream);
        Assert.Equal(3, packet.HotbarSlot);
        Assert.Equal(2, packet.ActorRuntimeId);
        Assert.Equal((short)1, packet.Item.NetworkId);
        Assert.Equal((ushort)1, packet.Item.Count);
        Assert.Equal(47, packet.Item.StackNetworkId);
    }

    [Fact]
    public void MobEquipment_changes_main_hand_only_when_the_claimed_slot_matches_authority()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("equip-valid");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Air, 0));
        player.SelectedHotbarSlot = 0;

        var writer = new BinaryStream();
        writer.WriteUnsignedVarLong(player.RuntimeId);
        NetworkItemStack.Empty.WriteNetworkItemStackDescriptor(ref writer);
        writer.WriteByte(4); // inventory slot
        writer.WriteByte(4); // hotbar slot
        writer.WriteByte(MobEquipmentPacket.WindowInventory);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.MOB_EQUIPMENT_PACKET },
            ref stream));

        Assert.Equal(4, player.SelectedHotbarSlot);
    }

    [Fact]
    public void MobEquipment_rejects_a_mismatched_held_stack_without_changing_main_hand()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("equip-mismatch");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Stone, 1));
        player.SelectedHotbarSlot = 0;

        var writer = new BinaryStream();
        writer.WriteUnsignedVarLong(player.RuntimeId);
        NetworkItemStack.Empty.WriteNetworkItemStackDescriptor(ref writer); // contradicts stone in slot 4
        writer.WriteByte(4);
        writer.WriteByte(4);
        writer.WriteByte(MobEquipmentPacket.WindowInventory);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.MOB_EQUIPMENT_PACKET },
            ref stream));

        Assert.Equal(0, player.SelectedHotbarSlot);
    }

    [Fact]
    public void InventoryTransaction_actor_attack_submits_gameplay_attack_intent()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorattack");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Air, 0));
        var writer = new BinaryStream();
        writer.WriteVarInt(0); // legacy request id
        writer.WriteBool(false); // legacy slots absent
        writer.WriteBool(true); // transaction type present
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteBool(true); // actions present
        writer.WriteUnsignedVarInt(0); // no legacy actions
        writer.WriteUnsignedVarLong(99); // target actor runtime id
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4); // hotbar slot
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
        new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream);

        Assert.True(handled);
        Assert.Equal(4, player.SelectedHotbarSlot);
        Assert.False(player.TryConsumeAttackIntent(100));
        Assert.True(player.TryConsumeAttackIntent(99));
    }

    [Fact]
    public void InventoryTransaction_actor_attack_damages_the_named_actor_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorattack-damage");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Air, 0));

        var zombies = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var zombie = zombies.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(zombies.Stores.Identities.TryGet(zombie, out var identity));

        var writer = new BinaryStream();
        writer.WriteVarInt(0); // legacy request id
        writer.WriteBool(false); // legacy slots absent
        writer.WriteBool(true); // transaction type present
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteBool(true); // actions present
        writer.WriteUnsignedVarInt(0); // no legacy actions
        writer.WriteUnsignedVarLong(checked((long)identity.ActorRuntimeId));
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4);
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        zombies.Tick(fx.Clock, fx.Players.Online);

        Assert.True(zombies.Stores.Health.TryGet(zombie, out var health));
        Assert.Equal(16f, health.State.Current);
    }

    [Fact]
    public void InventoryTransaction_actor_attack_damages_the_named_player_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("actorattack-player");
        var target = fx.AddInGamePlayer("actorattack-target");
        Assert.True(attacker.Inventory.TrySetBlock(4, Blocks.Air, 0));
        target.PositionX = attacker.PositionX + 1;
        target.PositionZ = attacker.PositionZ;

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarLong(target.RuntimeId);
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4);
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            attacker.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(16f, target.Health);
    }

    [Fact]
    public void InventoryTransaction_actor_interact_uses_the_wire_runtime_id_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorinteract-runtime");
        var wheat = fx.Context.ItemPalette.Require("minecraft:wheat");
        Assert.True(player.Inventory.TrySetItem(0, wheat, 1));
        player.SelectedHotbarSlot = 0;

        var cows = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var cow = cows.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);
        ref var identity = ref cows.Stores.Identities.GetRef(cow);
        identity.ActorRuntimeId = checked((ulong)(identity.ActorUniqueId + 10_000));
        ref var cowState = ref cows.CowStates.GetRef(cow);
        cowState.BreedCooldownUntilTick = 5_000;
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarLong(checked((long)identity.ActorRuntimeId));
        writer.WriteVarInt(InventoryTransactionPacket.ActorInteract);
        writer.WriteVarInt(0);
        held.WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        cows.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, cows.FeedCount);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }

    [Fact]
    public void InventoryTransaction_actor_attack_rejects_a_held_item_mismatch_without_changing_selection()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("actorattack-mismatch");
        Assert.True(player.Inventory.TrySetBlock(4, Blocks.Stone, 1));
        player.SelectedHotbarSlot = 0;

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarLong(99);
        writer.WriteVarInt(InventoryTransactionPacket.ActorAttack);
        writer.WriteVarInt(4);
        WriteNetworkItemAir(ref writer); // contradicts the authoritative stone in slot 4
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        Assert.Equal(0, player.SelectedHotbarSlot);
        Assert.False(player.TryConsumeAttackIntent(99));
    }

    [Fact]
    public void InventoryTransaction_release_updates_hotbar_without_creating_a_second_use_intent()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("release-slot");
        Assert.True(player.Inventory.TrySetBlock(5, Blocks.Air, 0));
        player.SelectedHotbarSlot = 0;
        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemRelease);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(0); // release
        writer.WriteVarInt(5); // hotbar slot
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 3; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        Assert.Equal(5, player.SelectedHotbarSlot);
        Assert.False(player.TryConsumeEatIntent());
        Assert.False(player.TryConsumeProjectileIntent());
    }

    [Fact]
    public void InventoryTransaction_unknown_typed_actions_do_not_change_selected_hotbar_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("unknown-transaction-action");
        Assert.True(player.Inventory.TrySetBlock(5, Blocks.Stone, 1));
        player.SelectedHotbarSlot = 0;
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 5);

        var use = new BinaryStream();
        use.WriteVarInt(0);
        use.WriteBool(false);
        use.WriteBool(true);
        use.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        use.WriteBool(true);
        use.WriteUnsignedVarInt(0);
        use.WriteVarInt(99); // unknown UseItem action
        use.WriteByte(0);
        use.WriteVarInt(0);
        use.WriteVarInt(0);
        use.WriteVarInt(0);
        use.WriteByte(0);
        use.WriteVarInt(5);
        held.WriteNetworkItemStackDescriptor(ref use);
        for (var i = 0; i < 6; i++) use.WriteFloat(0, BinaryStream.Endianess.Little);
        use.WriteUnsignedVarInt(0);
        use.WriteByte(0);
        use.WriteByte(0);
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref use));
        Assert.Equal(0, player.SelectedHotbarSlot);

        var actor = new BinaryStream();
        actor.WriteVarInt(0);
        actor.WriteBool(false);
        actor.WriteBool(true);
        actor.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUseOnActor);
        actor.WriteBool(true);
        actor.WriteUnsignedVarInt(0);
        actor.WriteUnsignedVarLong(99);
        actor.WriteVarInt(99); // unknown UseItemOnActor action
        actor.WriteVarInt(5);
        held.WriteNetworkItemStackDescriptor(ref actor);
        for (var i = 0; i < 6; i++) actor.WriteFloat(0, BinaryStream.Endianess.Little);
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref actor));
        Assert.Equal(0, player.SelectedHotbarSlot);

        var release = new BinaryStream();
        release.WriteVarInt(0);
        release.WriteBool(false);
        release.WriteBool(true);
        release.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemRelease);
        release.WriteBool(true);
        release.WriteUnsignedVarInt(0);
        release.WriteVarInt(99); // unknown Release action
        release.WriteVarInt(5);
        held.WriteNetworkItemStackDescriptor(ref release);
        for (var i = 0; i < 3; i++) release.WriteFloat(0, BinaryStream.Endianess.Little);
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref release));
        Assert.Equal(0, player.SelectedHotbarSlot);
    }

    [Fact]
    public void InventoryTransaction_use_click_air_relays_animation_without_creating_targetless_melee()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("air-swing");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Air, 0));

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(InventoryTransactionPacket.UseClickAir);
        writer.WriteByte(0);
        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteByte(0);
        writer.WriteVarInt(0);
        WriteNetworkItemAir(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0);
        writer.WriteByte(0);
        writer.WriteByte(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        Assert.False(player.TryConsumeAttackIntent());
    }

    [Fact]
    public void InventoryTransaction_click_block_rejects_a_stale_clicked_runtime_before_placing()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stale-click-block");
        StandForPlace(player, 10, 64, 10);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 2));
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(InventoryTransactionPacket.UseClickBlock);
        writer.WriteByte(0);
        writer.WriteVarInt(9);
        writer.WriteVarInt(64);
        writer.WriteVarInt(10);
        writer.WriteByte(5); // place on the +X face, at 10,64,10
        writer.WriteVarInt(0);
        held.WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(Blocks.Stone); // client predicted stone; server has air
        writer.WriteByte(0);
        writer.WriteByte(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(10, 64, 10));
        Assert.Equal(2, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void InventoryTransaction_click_block_accepts_an_unspecified_clicked_runtime()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("unspecified-click-runtime");
        StandForPlace(player, 10, 64, 10);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 2));
        var held = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteVarInt(InventoryTransactionPacket.UseClickBlock);
        writer.WriteByte(0);
        writer.WriteVarInt(9);
        writer.WriteVarInt(64);
        writer.WriteVarInt(10);
        writer.WriteByte(5);
        writer.WriteVarInt(0);
        held.WriteNetworkItemStackDescriptor(ref writer);
        for (var i = 0; i < 6; i++)
            writer.WriteFloat(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0); // target state omitted by this client
        writer.WriteByte(0);
        writer.WriteByte(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(10, 64, 10));
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void InventoryTransaction_mismatch_resends_authoritative_inventory_view()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mismatch");
        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out _)) { }

        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeMismatch);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));
        FlushRaknet(fx.Players);

        Assert.NotEmpty(fx.Transport.Captured);
    }

    [Fact]
    public void Legacy_normal_transaction_drop_commits_once_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("legacy-drop");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        var before = player.Session.Protocol.Inventory.DescribeSlot(player.Inventory, 0);
        var after = before with { Count = 3 };
        var dropped = before with { Count = 2 };

        var writer = new BinaryStream();
        writer.WriteVarInt(-2); // correlatable legacy request ID
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0); // no requested slot replay entries
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeNormal);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(2);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceContainer,
            windowId: 0,
            slot: 0,
            before,
            after);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceWorld,
            windowId: null,
            slot: 0,
            NetworkItemStack.Empty,
            dropped);

        var bytes = writer.GetBufferDisposing().ToArray();
        for (var i = 0; i < 2; i++)
        {
            var stream = new BinaryStream(bytes);
            Assert.True(new InGameSessionHandler().HandleDataPacket(
                player.Session,
                new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
                ref stream));
        }

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(3, player.Inventory.Get(0).Count); // duplicate packet must not remove another two
        Assert.Equal(2, fx.World.FloorDrops.Snapshot()
            .Where(drop => drop.Id == StackId.FromBlock(Blocks.Stone))
            .Sum(drop => drop.Count));
    }

    [Fact]
    public void Legacy_normal_transaction_drop_rejects_a_stale_advertised_stack_id()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("legacy-drop-stale");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        var before = player.Session.Protocol.Inventory.DescribeForWire(
            InventorySlotReference.Player(0), player.Inventory.Get(0));
        Assert.True(before.StackNetworkId > 0);
        var stale = before with { StackNetworkId = before.StackNetworkId + 1 };
        var after = before with { Count = 3 };
        var dropped = before with { Count = 2 };

        var writer = new BinaryStream();
        writer.WriteVarInt(-2);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeNormal);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(2);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceContainer,
            windowId: 0,
            slot: 0,
            stale,
            after);
        WriteNormalInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceWorld,
            windowId: null,
            slot: 0,
            NetworkItemStack.Empty,
            dropped);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INVENTORY_TRANSACTION_PACKET },
            ref stream));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Empty(fx.World.FloorDrops.Snapshot());
    }

    private static void WriteNetworkItemAir(ref BinaryStream writer)
    {
        writer.WriteShort(0, BinaryStream.Endianess.Little);
        writer.WriteUShort(0, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0);
        writer.WriteBool(false);
        writer.WriteUnsignedVarInt(0);
        writer.WriteUnsignedVarInt(0);
    }

    private static void WriteNormalInventoryAction(
        ref BinaryStream writer,
        uint sourceType,
        byte? windowId,
        int slot,
        NetworkItemStack oldItem,
        NetworkItemStack newItem)
    {
        writer.WriteUnsignedVarInt(checked((int)sourceType));
        writer.WriteBool(true); // required WindowID marker
        writer.WriteBool(windowId.HasValue);
        if (windowId is { } window)
            writer.WriteByte(window);
        writer.WriteBool(true); // required SourceFlags marker
        writer.WriteBool(false);
        writer.WriteUnsignedVarInt(slot);
        oldItem.WriteNetworkItemStackDescriptor(ref writer);
        newItem.WriteNetworkItemStackDescriptor(ref writer);
    }

    [Fact]
    public void Chat_pending_is_consumed_by_ChatSystem_tick()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        _ = fx.AddInGamePlayer("bob");

        Assert.True(alice.SubmitChat("hello"));
        Assert.True(alice.TryConsumeChat(out var msg));
        Assert.Equal("hello", msg);

        Assert.True(alice.SubmitChat("hello"));
        var before = fx.Transport.Captured.Count;
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(alice.TryConsumeChat(out _));
        Assert.True(fx.Transport.Captured.Count > before);
    }

    [Fact]
    public void Chat_fifo_drains_all_pending_on_tick()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        _ = fx.AddInGamePlayer("bob");

        Assert.True(alice.SubmitChat("one"));
        Assert.True(alice.SubmitChat("two"));
        Assert.True(alice.SubmitChat("three"));
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.False(alice.TryConsumeChat(out _));
    }

    [Fact]
    public void Chat_fifo_rejects_when_full()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        for (var i = 0; i < Player.Player.MaxPendingChat; i++)
            Assert.True(alice.SubmitChat($"m{i}"));
        Assert.False(alice.SubmitChat("overflow"));
    }

    [Fact]
    public void ChatSystem_preserves_pending_chat_until_player_is_in_game()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("pre-spawn", isInGame: false);
        Assert.True(player.SubmitChat("not-yet"));

        new ChatSystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.TryConsumeChat(out var message));
        Assert.Equal("not-yet", message);
    }

    [Fact]
    public void ChatSystem_fans_out_on_tick_without_handler_path()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var bob = fx.AddInGamePlayer("bob");
        alice.Session.Profile = new ClientProfile("25332747913222912", "dev", 7, "plat");

        alice.SubmitChat("ping");
        var datagramsBefore = fx.Transport.Captured.Count;

        // InGameSessionHandler only SubmitChat; fan-out is ChatSystem's job.
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(alice.TryConsumeChat(out _));
        Assert.True(fx.Transport.Captured.Count > datagramsBefore,
            "ChatSystem should SendChat to in-game peers via RakNet.");

        var joined = ConcatCaptured(fx);
        Assert.Contains("25332747913222912"u8.ToArray(), joined);

        bob.SubmitChat("pong");
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.False(bob.TryConsumeChat(out _));
    }

    [Theory]
    [InlineData("survival", 0)]
    [InlineData("creative", 1)]
    [InlineData("s", 0)]
    [InlineData("c", 1)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("SURVIVAL", 0)]
    public void GameModeConfig_parses_known_args(string arg, int expected)
    {
        Assert.True(GameModeConfig.TryParseArg(arg, out var mode));
        Assert.Equal((GameMode)expected, mode);
    }

    [Theory]
    [InlineData("/gamemode creative", 1, false)]
    [InlineData("/gamemode s", 0, false)]
    [InlineData("/GAMEMODE 1", 1, false)]
    [InlineData("gamemode creative", 1, false)]
    [InlineData("gamemode c", 1, false)]
    public void GameModeConfig_parses_command_line(string line, int expected, bool expectBad)
    {
        var ok = GameModeConfig.TryParseCommand(line, out var mode, out var bad);
        Assert.Equal(!expectBad, ok);
        Assert.Equal(expectBad, bad);
        if (ok) Assert.Equal((GameMode)expected, mode);
    }

    [Theory]
    [InlineData("/gamemode")]
    [InlineData("/gamemode xyz")]
    [InlineData("/gamemode adventure")]
    public void GameModeConfig_bad_gamemode_args_set_badArgs(string line)
    {
        Assert.False(GameModeConfig.TryParseCommand(line, out _, out var bad));
        Assert.True(bad);
    }

    [Theory]
    [InlineData("/help")]
    [InlineData("/tp 0 0 0")]
    [InlineData("hello")]
    public void GameModeConfig_non_gamemode_lines_are_not_commands(string line)
    {
        Assert.False(GameModeConfig.TryParseCommand(line, out _, out var bad));
        Assert.False(bad);
    }

    [Fact]
    public void GameMode_intent_overwrite_latest_and_system_applies_without_chat_fanout()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice", GameMode.Survival);
        var bob = fx.AddInGamePlayer("bob", GameMode.Survival);
        Assert.True(alice.Inventory.TrySetBlock(0, Blocks.Stone, 3));

        alice.SubmitGameMode(GameMode.Creative);
        alice.SubmitGameMode(GameMode.Survival); // overwrite-latest
        alice.SubmitGameMode(GameMode.Creative);

        var before = fx.Transport.Captured.Count;
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Equal(GameMode.Creative, alice.GameMode);
        Assert.Equal(GameMode.Survival, bob.GameMode);
        Assert.False(alice.TryConsumeGameMode(out _));
        Assert.Equal(Blocks.Stone, alice.Inventory.Get(0).Id.Value);
        Assert.Equal(3, alice.Inventory.Get(0).Count);
        Assert.True(fx.Transport.Captured.Count > before,
            "GameModeSystem should transmit SetPlayerGameType/abilities to self.");

        // Slash path must not use SubmitChat — peer must not get a chat fan-out from mode switch.
        Assert.False(alice.TryConsumeChat(out _));
        new ChatSystem().Tick(fx.Clock, fx.Players.Online);
        Assert.False(bob.TryConsumeChat(out _));
    }

    [Fact]
    public void GameModeSystem_refresh_peer_view_sends_to_other_InGame_peers()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice", GameMode.Survival);
        var bob = fx.AddInGamePlayer("bob", GameMode.Survival);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        alice.SubmitGameMode(GameMode.Creative);
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Equal(GameMode.Creative, alice.GameMode);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "GameModeSystem RefreshPeerView should send RemoveActor+AddPlayer toward peers.");
        _ = bob;
    }

    [Fact]
    public void PlayerVisibility_RefreshPeerView_sends_without_PlayerList_churn()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice", GameMode.Survival);
        var bob = fx.AddInGamePlayer("bob", GameMode.Survival);
        alice.Session.Profile = new ClientProfile("xuid-a", "dev-a", 7, "plat-a");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        alice.SetGameMode(GameMode.Creative);
        PlayerVisibility.RefreshPeerView(alice, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 1);
        var joined = ConcatCaptured(fx);
        Assert.Contains("dev-a"u8.ToArray(), joined);
        _ = bob;
    }

    [Fact]
    public void GameModeSystem_remints_creative_content_on_every_mode_change()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("switcher", GameMode.Survival);
        var creativePayload = InventoryProtocol.BuildCreativeContent(
            fx.Context.Creative, fx.Context.ItemPalette).Encode().ToArray();

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.SubmitGameMode(GameMode.Creative);
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.Equal(GameMode.Creative, player.GameMode);
        Assert.Contains(creativePayload, ConcatCaptured(fx));

        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.SubmitGameMode(GameMode.Survival);
        new GameModeSystem().Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Assert.Equal(GameMode.Survival, player.GameMode);
        Assert.Contains(creativePayload, ConcatCaptured(fx));
    }

    [Fact]
    public void InventorySystem_applies_swap_intent_on_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mover");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.GrassBlock, 2));

        var intent = InventoryStackIntent.Create(42, [InventoryStackAction.Swap(0, 9)]);
        Assert.True(player.SubmitInventoryStack(intent));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.GrassBlock, player.Inventory.Get(0).Id.Value);
        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(9).Id.Value);
        Assert.Equal(5, player.Inventory.Get(9).Count);
        Assert.False(player.TryConsumeInventoryStack(out _));
    }

    [Fact]
    public void InventorySystem_cursor_take_then_place()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dragger");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 8));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(0, PlayerInventory.CursorSlot, 3)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Equal(3, player.Inventory.Cursor.Count);

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.Transfer(PlayerInventory.CursorSlot, 9, 3)
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

        for (var i = 0; i < Player.Player.MaxPendingInventoryStacks; i++)
            Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(i, [
                InventoryStackAction.Swap(0, 1)
            ])));

        Assert.False(player.SubmitInventoryStack(InventoryStackIntent.Create(99, [
            InventoryStackAction.Swap(0, 2)
        ])));
    }

    [Fact]
    public void InventorySystem_rolls_back_failed_transfer()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("failmove");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.GrassBlock, 3));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(7, [
            InventoryStackAction.Transfer(0, 9, 1)
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

        for (var i = 0; i < Player.Player.MaxPendingInventoryStacks; i++)
            Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(i, [
                InventoryStackAction.Swap(0, 1)
            ])));

        Assert.False(player.SubmitInventoryStack(InventoryStackIntent.Create(99, [
            InventoryStackAction.Swap(0, 2)
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
        Assert.Equal(1, alice.LastReplicatedHotbarSlot);
        Assert.Equal(StackId.FromBlock(Blocks.GrassBlock), alice.LastReplicatedHeldStackId);
        Assert.Equal(5, alice.LastReplicatedHeldCount);
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

        Assert.True(alice.SubmitInventoryStack(InventoryStackIntent.Create(900, [
            InventoryStackAction.Swap(0, 9)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        equipment.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(StackId.FromBlock(Blocks.Stone), alice.LastReplicatedHeldStackId);
        Assert.Equal(2, alice.LastReplicatedHeldCount);
    }

    /// <summary>Priority.Normal queues frames; Tick flushes OutputFrames to Server.Send.</summary>
    private static void FlushRaknet(PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }

    private static byte[] ConcatCaptured(IntentTestFixture fx)
    {
        var total = 0;
        foreach (var chunk in fx.Transport.Captured)
            total += chunk.Length;
        var buf = new byte[total];
        var o = 0;
        foreach (var chunk in fx.Transport.Captured)
        {
            chunk.CopyTo(buf, o);
            o += chunk.Length;
        }

        return buf;
    }

    [Fact]
    public void SetBlock_during_BlockSystem_tick_updates_ram_without_blocking()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fast");
        StandForPlace(player, 9, 64, 9);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 3));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(9, 64, 9, Blocks.Stone, hotbarSlot: 0)));

        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(9, 64, 9));
    }

    [Fact]
    public void BlockSystem_place_chest_ensures_store()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chestor");
        StandForPlace(player, 4, 64, 4);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Chest, 2));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(4, 64, 4, Blocks.Chest, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
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
        fx.World.Chests.Ensure(5, 64, 5);

        // Starter kit already has chests in hotbar slot 5 — merge target.
        var before = player.Inventory.Get(5).Count;

        QueueReadyBreak(fx.Clock, fx.World, player, 5, 64, 5);
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(5, 64, 5));
        Assert.False(fx.World.Chests.TryGetSlots(5, 64, 5, out _));
        Assert.Equal(Blocks.Chest, player.Inventory.Get(5).Id.Value);
        Assert.Equal(before + 1, player.Inventory.Get(5).Count);
    }

    [Fact]
    public void InventorySystem_transfers_to_open_chest_flat()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("loot");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        fx.World.Chests.Ensure(1, 64, 1);
        player.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        StandNear(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(
                0, InventoryContainerMap.ChestBase, 4,
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
        fx.World.Chests.Ensure(1, 64, 1);
        player.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        player.PositionX = 100;
        player.PositionY = 64;
        player.PositionZ = 100;
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.Transfer(0, InventoryContainerMap.ChestBase, 4)
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
        fx.World.Chests.Ensure(1, 64, 1);
        fx.World.Chests.Ensure(3, 64, 1);
        StandNear(player, 3, 64, 1);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        var chestA = player.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(50, [
            InventoryStackAction.Transfer(0, InventoryContainerMap.ChestBase, 4)
        ], chestA.Generation)));

        // A close/reopen was processed before the queued action. The packet referenced chest A;
        // resolving it against the current chest B would be a cross-container injection.
        var chestB = player.OpenChestContainer(2, 0, OpenChestView.Single(3, 64, 1));
        Assert.True(chestB.Generation > chestA.Generation);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10, player.Inventory.Get(0).Count);
        Assert.True(fx.World.Chests.Get(1, 64, 1, 0).IsEmpty);
        Assert.True(fx.World.Chests.Get(3, 64, 1, 0).IsEmpty);
    }

    [Fact]
    public void ItemStackRequest_rejects_crafting_when_player_inventory_ui_is_not_open()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("closed-craft");
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 501, RecipeRegistry.OakLogToPlanks);

        Assert.False(player.TryConsumeInventoryStack(out _));
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void ItemStackRequest_after_Interact_open_inventory_commits_on_the_same_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-then-creative", GameMode.Creative);

        // The client may send the first creative request before the GameLoop has materialised
        // the ContainerOpen. Both packets are correctly ordered on the inbound stream.
        var interact = new BinaryStream();
        interact.WriteByte(InteractPacket.ActionOpenInventory);
        interact.WriteUnsignedVarLong(player.RuntimeId);
        interact.WriteBool(false); // no position
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
            ref interact));

        var request = new BinaryStream();
        request.WriteUnsignedVarInt(1); // request count
        request.WriteVarInt(613);
        request.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref request, ItemStackRequestPacket.ActionCraftCreative);
        request.WriteUnsignedVarInt(checked((int)CreativeCatalog.Stone));
        request.WriteByte(1); // number of crafts (wire boilerplate)
        request.WriteUnsignedVarInt(0); // filter strings
        request.WriteInt(0, BinaryStream.Endianess.Little); // filter cause
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref request));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.InventoryWindowOpen);
        Assert.Equal(StackId.FromBlock(Blocks.Stone), player.CraftUi.Result.Id);
        Assert.Equal(PlayerInventory.MaxStack, player.CraftUi.Result.Count);
    }

    [Fact]
    public void ItemStackRequest_after_pending_chest_open_commits_on_the_same_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-chest-then-transfer");
        fx.World.SetBlock(2, 64, 2, Blocks.Chest);
        fx.World.Chests.Ensure(2, 64, 2);
        StandNear(player, 2, 64, 2);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 2));

        // The block interaction publishes its window request; the immediately following ISR
        // must bind to that pending chest rather than be rejected for an absent current view.
        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 64, 2)));
        SubmitChestTransferRequest(player, requestId: 614);

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(StackId.FromBlock(Blocks.Dirt), fx.World.Chests.Get(2, 64, 2, 0).Id);
        Assert.Equal(1, fx.World.Chests.Get(2, 64, 2, 0).Count);
    }

    /// <summary>Regression for ADR §139 — interacting with a crafting table opens a real workbench window, distinct from both chest and the personal inventory.</summary>
    [Fact]
    public void Interacting_with_a_crafting_table_opens_and_closes_a_workbench_window()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("workbench-user");
        fx.World.SetBlock(2, 64, 2, Blocks.CraftingTable);
        StandNear(player, 2, 64, 2);
        var sys = fx.CreateInventorySystem();

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenCraftingTable(2, 64, 2)));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsAtCraftingTable);
        Assert.True(player.OpenContainer is not null);
        var session = player.OpenContainer!.Value;
        Assert.Equal(InventoryContainerMap.WindowTypeWorkbench, session.WindowType);

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.Close(session.WindowId, session.WindowType)));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsAtCraftingTable);
        Assert.Null(player.OpenContainer);
    }

    [Fact]
    public void ItemStackRequest_rejects_a_dynamic_container_id_for_a_static_hotbar_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dynamic-container");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 2));
        var slotOneBefore = player.Inventory.Get(1);

        var request = new BinaryStream();
        request.WriteUnsignedVarInt(1);
        request.WriteVarInt(615);
        request.WriteUnsignedVarInt(1);
        WriteStackActionHeader(ref request, ItemStackRequestPacket.ActionPlace);
        request.WriteByte(1);
        WriteDynamicSlotInfo(ref request, InventoryContainerMap.Hotbar, 0, dynamicId: 42);
        WriteSlotInfo(ref request, InventoryContainerMap.Hotbar, 1);
        SubmitItemStackRequest(player, request);

        Assert.False(player.TryConsumeInventoryStack(out _));
        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.Equal(slotOneBefore, player.Inventory.Get(1));
    }

    [Fact]
    public void ItemStackRequest_crafting_bound_to_closed_player_inventory_ui_does_not_commit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stale-craft");
        var session = player.OpenPlayerContainer(0, 0xff);
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 502, RecipeRegistry.OakLogToPlanks);
        Assert.True(player.TryCloseContainer(session.WindowId, session.WindowType, out _));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void ItemStackRequest_crafting_bound_to_replaced_player_inventory_ui_does_not_commit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("replaced-craft");
        player.OpenPlayerContainer(0, 0xff);
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 503, RecipeRegistry.OakLogToPlanks);
        player.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void ItemStackRequest_rejects_crafting_while_chest_is_the_active_session()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chest-craft");
        player.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 504, RecipeRegistry.OakLogToPlanks);

        Assert.False(player.TryConsumeInventoryStack(out _));
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void ItemStackRequest_rejects_cursor_drop_without_an_active_container_session()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("closed-cursor");

        SubmitDropRequest(player, requestId: 505, InventoryContainerMap.Cursor, 0);

        Assert.False(player.TryConsumeInventoryStack(out _));
    }

    [Fact]
    public void ItemStackRequest_rejects_a_chest_slot_when_player_inventory_ui_is_active()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("wrong-chest");
        player.OpenPlayerContainer(0, 0xff);

        SubmitChestTransferRequest(player, requestId: 506);

        Assert.False(player.TryConsumeInventoryStack(out _));
    }

    [Fact]
    public void ItemStackRequest_rejects_a_request_that_spans_chest_and_crafting_ui()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mixed-ui");
        player.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));

        SubmitChestAndCraftRequest(player, requestId: 507, RecipeRegistry.OakLogToPlanks);

        Assert.False(player.TryConsumeInventoryStack(out _));
    }

    [Fact]
    public void InventorySystem_cancels_pending_window_open_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-window");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(1, 64, 1)));

        // Models a disconnect after GameLoop captured Online but before InventorySystem runs.
        player.IsInGame = false;
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Null(player.OpenContainer);
        Assert.Equal(0, fx.World.Chests.OpenerCount(1, 64, 1));
    }

    [Fact]
    public void InventorySystem_cancels_pending_mutation_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-stack");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 2));
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(508, [
            InventoryStackAction.Transfer(0, 9, 1)
        ])));

        // The request was accepted before teardown but has no authority after disconnect.
        player.IsInGame = false;
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.True(player.Inventory.Get(9).IsEmpty);
    }

    [Fact]
    public void BlockEditSystem_cancels_pending_world_mutation_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-edit");
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Dirt)));

        player.IsInGame = false;
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(1, 64, 1));
    }

    [Fact]
    public void BlockDigSystem_cancels_pending_dig_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-dig");
        Assert.True(player.SubmitDigStart(1, 64, 1, fx.Clock.CurrentTick, requiredTicks: 20));

        player.IsInGame = false;
        new BlockDigSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.HasBreakTarget);
    }

    [Fact]
    public void MovementSystem_cancels_pending_input_after_player_leaves_gameplay()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-movement");
        var originalY = player.PositionY;
        player.SubmitMovementInput(MovementInputState.From(
            x: 20f, y: MovementSystem.VoidRescueY - 1f, z: 20f, pitch: 0f, yaw: 0f));

        player.IsInGame = false;
        new MovementSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0f, player.PositionX);
        Assert.Equal(originalY, player.PositionY);
        Assert.Equal(0f, player.PositionZ);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void InventorySystem_rejects_open_chest_transfer_after_target_is_removed()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stalechest");
        fx.World.SetBlock(1, 64, 1, Blocks.Chest);
        fx.World.Chests.Ensure(1, 64, 1);
        player.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        StandNear(player, 1, 64, 1);
        fx.World.SetBlock(1, 64, 1, Blocks.Air);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.Transfer(0, InventoryContainerMap.ChestBase, 4)
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
        fx.World.Chests.Ensure(1, 64, 1);
        fx.World.Chests.Ensure(2, 64, 1);
        player.OpenChestContainer(2, 0, OpenChestView.Double(new ChestPair(1, 64, 1, 2, 64, 1)));
        StandNear(player, 1, 64, 1);
        fx.World.SetBlock(2, 64, 1, Blocks.Air);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 10));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(4, [
            InventoryStackAction.Transfer(0, InventoryContainerMap.ChestBase, 4)
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
        player.OpenPlayerContainer(0, 0xff);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 8));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(
                0, InventoryContainerMap.CraftUiBase, 1,
                new WireSlot(InventoryContainerMap.CombinedHotbarAndInventory, 0),
                new WireSlot(InventoryContainerMap.CraftingInput, 28))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(7, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.OakLog, player.CraftUi.GetGrid(0).Id.Value);
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
    }

    [Fact]
    public void InventorySystem_drop_deposits_floor_entity()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropper");
        StandNear(player, 2, 64, 2);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 10));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.Drop(0, 4, new WireSlot(InventoryContainerMap.Hotbar, 0))
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
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 2)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(9, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.Equal(Blocks.OakLog, player.CraftUi.GetGrid(0).Id.Value);
        Assert.Equal(4, player.CraftUi.Result.Count);
        Assert.Equal(Blocks.OakPlanks, player.CraftUi.Result.Id.Value);
    }

    [Fact]
    public void InventorySystem_craft_then_take_result_to_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("takeout");
        player.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(10, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, 4,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Cursor.Id.Value);
        Assert.Equal(4, player.Inventory.Cursor.Count);
    }

    [Fact]
    public void InventorySystem_craft_chain_planks_then_chest_take()
    {
        // S35: log→planks take, place planks, chest craft, take chest (sequential intents).
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chaincraft");
        player.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 2));

        var sys = fx.CreateInventorySystem();

        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 1));
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(20, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 1, 4)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(1).Id.Value);
        Assert.Equal(4, player.Inventory.Get(1).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);

        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Air, 0));
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(21, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 2, 4)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(4, player.Inventory.Get(2).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);

        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.CraftUi.TrySetGrid(1, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.CraftUi.TrySetGrid(2, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.CraftUi.TrySetGrid(3, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.Inventory.TrySetBlock(1, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(2, Blocks.Air, 0));

        // ADR §139 regression: a chest's real vanilla shape (8 planks in a ring) needs a 3x3 grid —
        // the personal 2x2 grid must refuse it even though Zenith's shapeless matcher could
        // otherwise satisfy the aggregate count.
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(22, [
            InventoryStackAction.Craft(RecipeRegistry.OakPlanksToChest),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 3, 1,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 3))
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.NotEqual(Blocks.Chest, player.Inventory.Get(3).Id.Value);
        Assert.Equal(Blocks.OakPlanks, player.CraftUi.GetGrid(0).Id.Value); // rolled back, not consumed

        // Open a crafting table and place the same 8 planks in its 3x3 grid — now it succeeds.
        player.OpenCraftingTableContainer(1, InventoryContainerMap.WindowTypeWorkbench);
        Assert.True(player.TableCraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.TableCraftUi.TrySetGrid(1, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.TableCraftUi.TrySetGrid(2, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));
        Assert.True(player.TableCraftUi.TrySetGrid(3, InventorySlot.OfBlock(Blocks.OakPlanks, 2)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(23, [
            InventoryStackAction.Craft(RecipeRegistry.OakPlanksToChest),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 3, 1,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 3))
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.Chest, player.Inventory.Get(3).Id.Value);
        Assert.Equal(1, player.Inventory.Get(3).Count);
        Assert.True(player.TableCraftUi.GetGrid(0).IsEmpty); // consumed from the table grid, not the personal one
    }

    [Fact]
    public void InventorySystem_craft_refuses_while_result_occupied()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stuckout");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 2)));

        var sys = fx.CreateInventorySystem();
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(30, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.OakPlanks, player.CraftUi.Result.Id.Value);

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(31, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks)
        ])));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.OakPlanks, player.CraftUi.Result.Id.Value);
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
    }

    [Fact]
    public void InventorySystem_craft_times_two_then_place_result_to_bag()
    {
        // Shift-click craft output: CraftRecipe(times=2) + Place 8 planks to bag.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shiftcraft");
        player.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 2)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(12, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks, craftTimes: 2),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 9, 8,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 9))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.GetGrid(0).IsEmpty);
        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(9).Id.Value);
        Assert.Equal(8, player.Inventory.Get(9).Count);
    }

    [Fact]
    public void InventorySystem_craft_times_zero_treated_as_one()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("times0");
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(13, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks, craftTimes: 0),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.CraftUi.Result.Count);
        Assert.True(player.CraftUi.GetGrid(0).IsEmpty);
    }

    [Fact]
    public void InventorySystem_craft_times_clamped_by_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("clamp");
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        // Request 5 crafts but only 1 log → clamp to 1, not fail.
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(14, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks, craftTimes: 5),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(4, player.CraftUi.Result.Count);
        Assert.True(player.CraftUi.GetGrid(0).IsEmpty);
    }

    [Fact]
    public void InventorySystem_storage_slot_to_cursor_via_container_29()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("storage");
        player.OpenPlayerContainer(0, 0xff);
        Assert.True(player.Inventory.TrySetBlock(9, Blocks.Dirt, 8));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(11, [
            InventoryStackAction.Transfer(
                9, PlayerInventory.CursorSlot, 8,
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
        var intent = InventoryStackIntent.Create(712, [InventoryStackAction.Swap(0, 9)]);

        Assert.True(player.SubmitInventoryStack(intent));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(0).Id.Value);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(9).Id.Value);

        Assert.True(player.SubmitInventoryStack(intent));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(0).Id.Value);
        Assert.Equal(Blocks.Dirt, player.Inventory.Get(9).Id.Value);
    }

    [Fact]
    public void Creative_join_has_empty_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative", GameMode.Creative);
        Assert.Equal(GameMode.Creative, player.GameMode);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.Get(i).IsEmpty);
    }

    [Fact]
    public void BlockSystem_creative_place_does_not_consume_hotbar()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("cplace", GameMode.Creative);
        StandForPlace(player, 2, 64, 2);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 2, Blocks.Stone, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

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
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 3, Blocks.Air)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(3, 64, 3));
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void CreativeContent_starter_encode_has_items()
    {
        Blocks.EnsureLoaded();
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var packet = InventoryProtocol.BuildCreativeContent(CreativeCatalog.CreateDefault(), palette);
        Assert.Equal(25, packet.Items.Length); // ADR §138 + §139 added 5 building/decoration blocks.
        Assert.Equal(CreativeCatalog.Stone, packet.Items[0].CreativeItemNetworkId);
        Assert.Single(packet.Groups);
        Assert.True(packet.Encode().Length > 16);
    }

    [Fact]
    public void Interact_open_inventory_is_applied_by_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-inventory", GameMode.Creative);
        var writer = new BinaryStream();
        writer.WriteByte(InteractPacket.ActionOpenInventory);
        writer.WriteUnsignedVarLong(player.RuntimeId); // target is self
        writer.WriteBool(false); // no interaction position

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var handled = new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
            ref stream);

        Assert.True(handled);
        Assert.False(player.InventoryWindowOpen); // Session only publishes the intent.

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.InventoryWindowOpen);
        Assert.True(player.TryGetOpenContainerSession(out var open));
        Assert.Equal(OpenContainerSession.TargetKind.PlayerInventory, open.Target);
        Assert.Equal((byte)InventoryContainerMap.WindowInventory, open.WindowId);
        Assert.Equal(InventoryContainerMap.WindowTypeInventory, open.WindowType);
    }

    [Fact]
    public void Repeated_interact_open_inventory_does_not_reopen_the_player_container()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("repeat-open", GameMode.Creative);

        for (var i = 0; i < 2; i++)
        {
            var writer = new BinaryStream();
            writer.WriteByte(InteractPacket.ActionOpenInventory);
            writer.WriteUnsignedVarLong(player.RuntimeId);
            writer.WriteBool(false);

            var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
            Assert.True(new InGameSessionHandler().HandleDataPacket(
                player.Session,
                new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
                ref stream));
        }

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.TryGetOpenContainerSession(out var open));
        Assert.Equal((uint)1, open.Generation);
    }

    [Fact]
    public void Interact_open_inventory_rejects_a_non_self_target()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-inventory-forged", GameMode.Creative);
        var writer = new BinaryStream();
        writer.WriteByte(InteractPacket.ActionOpenInventory);
        writer.WriteUnsignedVarLong(player.RuntimeId + 1);
        writer.WriteBool(false);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
            ref stream));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.InventoryWindowOpen);
        Assert.False(player.TryGetOpenContainerSession(out _));
    }

    [Fact]
    public void Item_stack_request_creative_pick_to_hotbar_commits_through_the_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-wire", GameMode.Creative);
        player.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(73); // request id: protocol 2168 VarInt, not fixed little-endian
        writer.WriteUnsignedVarInt(2); // CraftCreative + Place

        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftCreative);
        writer.WriteUnsignedVarInt(checked((int)CreativeCatalog.Stone));
        writer.WriteByte(1); // NumberOfCrafts (protocol boilerplate)

        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(PlayerInventory.MaxStack);
        WriteSlotInfo(
            ref writer,
            InventoryContainerMap.CreatedOutput,
            InventoryContainerMap.CraftingResultWireSlot);
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);

        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        var slot = player.Inventory.Get(0);
        Assert.Equal(Blocks.Stone, slot.Id.Value);
        Assert.Equal(PlayerInventory.MaxStack, slot.Count);
        Assert.True(player.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void Item_stack_request_with_an_unbounded_wire_count_is_rejected_without_mutating_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("isr-bounded");
        var before = player.Inventory.Get(0);
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(ItemStackRequestPacket.MaxRequestsPerPacket + 1);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref stream));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(before, player.Inventory.Get(0));
    }

    [Fact]
    public void InventorySystem_craft_creative_places_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creator", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, PlayerInventory.MaxStack,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
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

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 0, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
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

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, 32)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Stone, player.Inventory.Cursor.Id.Value);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
        Assert.Equal(32, player.CraftUi.Result.Count);
    }

    [Fact]
    public void InventorySystem_craft_creative_rejects_different_item_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("clash", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(PlayerInventory.CursorSlot, Blocks.Dirt, 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(4, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
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

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(7, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
    }

    [Fact]
    public void InventorySystem_craft_creative_drop_from_result()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropcreate", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(5, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Drop(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.MaxStack,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
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

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(6, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }

    [Fact]
    public void InventorySystem_craft_creative_preserves_an_existing_created_output()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-pending-output", GameMode.Creative);
        Assert.True(player.CraftUi.TrySetResult(InventorySlot.OfBlock(Blocks.Dirt, 3)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(8, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        // A rejected pick must not replace or erase a result awaiting its original request.
        Assert.Equal(Blocks.Dirt, player.CraftUi.Result.Id.Value);
        Assert.Equal(3, player.CraftUi.Result.Count);
    }

    [Fact]
    public void ItemStackRequest_creative_destroy_removes_only_the_declared_source_count()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-destroy", GameMode.Creative);
        player.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(74);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionDestroy);
        writer.WriteByte(2);
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);

        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        var remaining = player.Inventory.Get(0);
        Assert.Equal(Blocks.Stone, remaining.Id.Value);
        Assert.Equal(3, remaining.Count);
    }

    [Fact]
    public void ItemStackRequest_destroy_rejects_survival_without_mutating_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("survival-destroy");
        player.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(75, [
            InventoryStackAction.Destroy(
                InventorySlotReference.Player(0), 2,
                new WireSlot(InventoryContainerMap.Hotbar, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(5, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void ItemStackRequest_mine_block_acknowledges_the_authoritative_hotbar_stack()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mine-stack-request");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(76);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionMineBlock);
        writer.WriteVarInt(0); // hotbar slot
        writer.WriteVarInt(0); // predicted durability: ignored until durability exists
        writer.WriteInt(0, BinaryStream.Endianess.Little); // no advertised stack id yet

        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        var slot = player.Inventory.Get(0);
        Assert.Equal(Blocks.Stone, slot.Id.Value);
        Assert.Equal(5, slot.Count);
    }

    [Fact]
    public void ItemStackRequest_rejects_create_for_an_unimplemented_result_slot_without_clearing_output()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("create-result-index");
        player.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.CraftUi.TrySetResult(InventorySlot.OfBlock(Blocks.Dirt, 3)));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(77);
        writer.WriteUnsignedVarInt(1);
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCreate);
        writer.WriteByte(1);
        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Dirt, player.CraftUi.Result.Id.Value);
        Assert.Equal(3, player.CraftUi.Result.Count);
    }

    [Fact]
    public void ItemStackRequest_rejects_zero_craft_times_without_consuming_the_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("zero-craft-times");
        player.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(78);
        writer.WriteUnsignedVarInt(1);
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftRecipe);
        writer.WriteUnsignedVarInt(checked((int)RecipeRegistry.OakLogToPlanks));
        writer.WriteByte(0);
        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.OakLog, player.CraftUi.GetGrid(0).Id.Value);
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void CreativeContent_empty_icon_uses_the_short_air_network_item_instance_descriptor()
    {
        // CreativeContent uses NetworkItemInstanceDescriptor. An air item (NetworkId 0) still
        // carries count/meta/block-runtime after its VarInt ID, but the trailing user-data blob
        // collapses to a single bare zero-length varint — no NBT-length/can-place/can-break
        // payload at all. Verified against gophertunnel's Writer.Item/itemUserData (protocol
        // 2168): a non-air item gets the full 10-byte empty-lists blob; NetworkId==0 does not.
        // The previous version of this test asserted the old, wrong 16-byte shape, which
        // corrupted every byte after CreativeContentBuilder's Construction-group icon (a real
        // air icon sent on every join) in the actual wire packet.
        var emptyPk = new CreativeContentPacket
        {
            Groups = [new CreativeGroupEntry(1, "", NetworkItemStack.Empty)],
            Items = []
        };
        var bytes = emptyPk.Encode().ToArray();
        // packet id + groups + category + name + short air item + items
        Assert.True(bytes.Length > 5);

        var writer = new BinaryStream();
        NetworkItemStack.Empty.WriteItemStack(ref writer);
        var air = writer.GetBufferDisposing().ToArray();
        Assert.Equal(6, air.Length); // network id + count + meta + block rid + bare zero-length user-data varint
    }

    [Fact]
    public void CreativeContent_non_air_icon_still_uses_the_full_empty_lists_descriptor()
    {
        // The counterpart to the air-item test above: a real (non-zero NetworkId) item stack
        // must still get the full NBT-length + can_place/can_break blob, matching
        // gophertunnel's Writer.Item for the present==true case.
        var writer = new BinaryStream();
        new NetworkItemStack(NetworkId: 5, Count: 1, BlockRuntimeId: 0).WriteItemStack(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal(5, stream.ReadVarInt()); // network id
        Assert.Equal((ushort)1, stream.ReadUShort(BinaryStream.Endianess.Little)); // count
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // meta
        Assert.Equal(0, stream.ReadVarInt()); // block runtime id
        Assert.Equal(10, stream.ReadUnsignedVarInt()); // extra-data length — full blob, not the air short-circuit
        for (var i = 0; i < 10; i++) Assert.Equal(0, stream.ReadByte());
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void CreativeContent_group_category_is_a_single_byte()
    {
        // ADR §85: category is a wire mapper<u8> (one byte), not a fixed int32 — found via
        // zenith-smoke-bot decoding CreativeContentPacket ("array size is abnormally large").
        var pk = new CreativeContentPacket
        {
            Groups = [new CreativeGroupEntry(CreativeContentPacket.CategoryConstruction, "", NetworkItemStack.Empty)],
            Items = []
        };
        var bytes = pk.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.CREATIVE_CONTENT_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(1, stream.ReadUnsignedVarInt()); // groups count
        Assert.Equal((byte)CreativeContentPacket.CategoryConstruction, stream.ReadByte()); // category — exactly one byte
        Assert.Equal("", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt()); // empty icon ID
        Assert.Equal((ushort)0, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // metadata
        Assert.Equal(0, stream.ReadVarInt()); // block runtime ID
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // air item: bare zero-length user-data varint, no NBT/list blob
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // items count
        Assert.True(stream.IsEndOfFile);
    }
}
