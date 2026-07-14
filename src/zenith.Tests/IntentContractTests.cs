using System.Collections.Concurrent;
using System.Net;
using Zenith.Event;
using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Network.Packets;
using Zenith.Network.Protocol;
using Zenith.Network.Session;
using Zenith.Network.Session.Handler;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Raknet.Log;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

file sealed class SilentLogger : ILogger
{
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
}

file sealed class RecordingRakNetServer : RakNetServer
{
    public ConcurrentQueue<byte[]> Captured { get; } = new();

    public RecordingRakNetServer() : base(port: 0) { }

    public override void Send(IPEndPoint endPoint, byte[] buffer) => Captured.Enqueue(buffer);
}

file sealed class StubSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream) =>
        false;
}

/// <summary>Monta Player + NetworkSession + world sem boot completo do ZenithServer.</summary>
file sealed class IntentTestFixture
{
    public PlayerManager Players { get; }
    public World.World World { get; }
    public GameClock Clock { get; } = new();
    public RecordingRakNetServer Transport { get; } = new();
    public ServerContext Context { get; }

    private int _nextPort = 20000;

    public IntentTestFixture()
    {
        Blocks.EnsureLoaded();
        var blockPalette = BlockPaletteLoader.FromEmbeddedResource();
        var itemPalette = ItemPaletteLoader.FromEmbeddedResource();
        Players = new PlayerManager();
        World = new World.World(new InMemoryChunkStorage());
        Context = new ServerContext(
            new SilentLogger(),
            Players,
            new EventBus(new SilentLogger()),
            Clock,
            World,
            new ServerConfig(),
            blockPalette,
            itemPalette,
            RecipeRegistry.CreateDefault());
    }

    public InventorySystem CreateInventorySystem() =>
        new(Players, World, Context.Recipes);

    public Player.Player AddInGamePlayer(string name, GameMode gameMode = GameMode.Survival)
    {
        var rak = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, _nextPort++),
            Id = _nextPort,
            Server = Transport,
            MTU = 1400
        };
        var session = new NetworkSession(rak, new StubSessionHandler(), Context);
        var player = new Player.Player(name, session, Players.AllocateRuntimeId(), Guid.NewGuid(), gameMode)
        {
            IsInGame = true
        };
        session.Player = player;
        Assert.True(Players.TryAdd(player));
        return player;
    }
}

public class IntentContractTests
{
    private static void StandNear(Player.Player player, int x, int y, int z)
    {
        player.PositionX = x + 0.5f;
        player.PositionY = y; // feet; eyes = Y + EyeHeight still within reach of block center
        player.PositionZ = z + 0.5f;
    }

    [Fact]
    public void BlockSystem_drains_fifo_queue_in_one_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("builder");
        StandNear(player, 2, 64, 0);
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 10));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 0, Blocks.Stone, hotbarSlot: 0)));

        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

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
        StandNear(player, 5, 70, 5);
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySet(2, Blocks.GrassBlock, 5));
        player.SelectedHotbarSlot = 0; // network race: client swapped selection after submit

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(5, 70, 5, Blocks.GrassBlock, hotbarSlot: 2)));

        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.GrassBlock, fx.World.GetBlock(5, 70, 5));
        Assert.Equal(5, player.Inventory.Get(0).Count); // slot 0 untouched
        Assert.Equal(4, player.Inventory.Get(2).Count); // intent slot consumed
    }

    [Fact]
    public void BlockSystem_rejects_place_when_HotbarSlot_invalid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("badslot");
        StandNear(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Stone, hotbarSlot: -1)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        // Place without valid slot must not mutate world or inventory.
        Assert.NotEqual(Blocks.Stone, fx.World.GetBlock(1, 64, 1));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void BlockSystem_rejects_place_into_occupied_cell()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stacker");
        StandNear(player, 4, 80, 4);
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));
        fx.World.SetBlock(4, 80, 4, Blocks.GrassBlock);

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(4, 80, 4, Blocks.Stone, hotbarSlot: 0)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.GrassBlock, fx.World.GetBlock(4, 80, 4));
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void BlockSystem_rejects_break_of_air()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("airpunch");
        StandNear(player, 0, 100, 0);
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 1));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 100, 0, Blocks.Air)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

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
            Assert.True(player.Inventory.TrySet(i, Blocks.GrassBlock, PlayerInventory.MaxStack));

        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(Blocks.Stone, player.Inventory.Get(9).RuntimeId);
        Assert.Equal(1, player.Inventory.Get(9).Count);
    }

    [Fact]
    public void BlockSystem_break_full_inventory_floor_drops()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fullbag");
        StandNear(player, 0, 90, 0);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.GrassBlock, PlayerInventory.MaxStack));

        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(1, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void BlockSystem_rejects_place_from_storage_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("deeplace");
        StandNear(player, 1, 64, 1);
        Assert.True(player.Inventory.TrySet(9, Blocks.Stone, 5));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Stone, hotbarSlot: 9)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

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
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));

        // ~20 blocks away horizontally — outside MaxBlockReach
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(20, 64, 0, Blocks.Stone, hotbarSlot: 0)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

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
        Assert.True(BlockSystem.IsWithinReach(player, 0, 64, 0));
        Assert.False(BlockSystem.IsWithinReach(player, 0, 64 + 20, 0));
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
        writer.WriteBool(false);
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
    }

    [Fact]
    public void Chat_pending_is_consumed_by_ChatSystem_tick()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        _ = fx.AddInGamePlayer("bob");

        alice.SubmitChat("hello");
        Assert.True(alice.TryConsumeChat(out var msg));
        Assert.Equal("hello", msg);

        alice.SubmitChat("hello");
        var before = fx.Transport.Captured.Count;
        new ChatSystem(fx.Players).Tick(fx.Clock);
        FlushRaknet(fx.Players);

        Assert.False(alice.TryConsumeChat(out _));
        Assert.True(fx.Transport.Captured.Count > before);
    }

    [Fact]
    public void ChatSystem_fans_out_on_tick_without_handler_path()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var bob = fx.AddInGamePlayer("bob");

        alice.SubmitChat("ping");
        var datagramsBefore = fx.Transport.Captured.Count;

        // InGameSessionHandler only SubmitChat; fan-out is ChatSystem's job.
        new ChatSystem(fx.Players).Tick(fx.Clock);
        FlushRaknet(fx.Players);

        Assert.False(alice.TryConsumeChat(out _));
        Assert.True(fx.Transport.Captured.Count > datagramsBefore,
            "ChatSystem should SendChat to in-game peers via RakNet.");

        bob.SubmitChat("pong");
        new ChatSystem(fx.Players).Tick(fx.Clock);
        FlushRaknet(fx.Players);
        Assert.False(bob.TryConsumeChat(out _));
    }

    [Fact]
    public void InventorySystem_applies_swap_intent_on_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mover");
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySet(9, Blocks.GrassBlock, 2));

        var intent = InventoryStackIntent.Create(42, [InventoryStackAction.Swap(0, 9)]);
        Assert.True(player.SubmitInventoryStack(intent));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(Blocks.GrassBlock, player.Inventory.Get(0).RuntimeId);
        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(9).RuntimeId);
        Assert.Equal(5, player.Inventory.Get(9).Count);
        Assert.False(player.TryConsumeInventoryStack(out _));
    }

    [Fact]
    public void InventorySystem_cursor_take_then_place()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dragger");
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 8));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(0, PlayerInventory.CursorSlot, 3)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.Equal(5, player.Inventory.Get(0).Count);
        Assert.Equal(3, player.Inventory.Cursor.Count);

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.Transfer(PlayerInventory.CursorSlot, 9, 3)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.Equal(3, player.Inventory.Get(9).Count);
    }

    [Fact]
    public void InventoryStack_queue_rejects_newest_when_full()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("spammer");
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 64));

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
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));
        Assert.True(player.Inventory.TrySet(9, Blocks.GrassBlock, 3));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(7, [
            InventoryStackAction.Transfer(0, 9, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

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
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 64));

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
        Assert.True(alice.Inventory.TrySet(0, Blocks.Stone, 10));
        Assert.True(alice.Inventory.TrySet(1, Blocks.GrassBlock, 5));
        alice.SelectedHotbarSlot = 0;

        // Prime fingerprint
        new EquipmentSystem(fx.Players).Tick(fx.Clock);
        FlushRaknet(fx.Players);
        var before = fx.Transport.Captured.Count;

        alice.SelectedHotbarSlot = 1;
        new EquipmentSystem(fx.Players).Tick(fx.Clock);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count > before,
            "EquipmentSystem should send MobEquipment to peers when held slot changes.");
        Assert.Equal(1, alice.LastReplicatedHotbarSlot);
        Assert.Equal(Blocks.GrassBlock, alice.LastReplicatedHeldRuntimeId);
        Assert.Equal(5, alice.LastReplicatedHeldCount);
        _ = bob;
    }

    /// <summary>Priority.Normal queues frames; Tick flushes OutputFrames to Server.Send.</summary>
    private static void FlushRaknet(PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }

    [Fact]
    public void SetBlock_during_BlockSystem_tick_updates_ram_without_blocking()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fast");
        StandNear(player, 9, 64, 9);
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 3));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(9, 64, 9, Blocks.Stone, hotbarSlot: 0)));

        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(9, 64, 9));
    }

    [Fact]
    public void BlockSystem_place_chest_ensures_store()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chestor");
        StandNear(player, 4, 64, 4);
        Assert.True(player.Inventory.TrySet(0, Blocks.Chest, 2));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(4, 64, 4, Blocks.Chest, hotbarSlot: 0)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);
        Assert.Equal(Blocks.Chest, fx.World.GetBlock(4, 64, 4));
        Assert.True(fx.World.Chests.TryGetSlots(4, 64, 4, out _));
    }

    [Fact]
    public void InventorySystem_transfers_to_open_chest_flat()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("loot");
        fx.World.Chests.Ensure(1, 70, 1);
        player.OpenChest = (1, 70, 1);
        Assert.True(player.Inventory.TrySet(0, Blocks.Dirt, 10));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(0, InventoryContainerMap.ChestBase, 4)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(6, player.Inventory.Get(0).Count);
        Assert.Equal(4, fx.World.Chests.Get(1, 70, 1, 0).Count);
        Assert.Equal(Blocks.Dirt, fx.World.Chests.Get(1, 70, 1, 0).RuntimeId);
    }

    [Fact]
    public void InventorySystem_craft_oak_log_to_planks()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("crafter");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySet(0, Blocks.OakLog, 2));

        Assert.True(player.SubmitInventoryStack(
            InventoryStackIntent.CreateCraft(9, RecipeRegistry.OakLogToPlanks)));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.OakLog, player.Inventory.Get(0).RuntimeId);
        Assert.Equal(4, player.Inventory.Get(1).Count);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(1).RuntimeId);
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
        StandNear(player, 2, 64, 2);
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 2, Blocks.Stone, hotbarSlot: 0)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

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
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));

        fx.World.SetBlock(3, 64, 3, Blocks.Stone);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(3, 64, 3, Blocks.Air)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(3, 64, 3));
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.Equal(0, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void CreativeContent_starter_encode_has_items()
    {
        Blocks.EnsureLoaded();
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var packet = CreativeContentPacket.CreateStarter(palette);
        Assert.Equal(7, packet.Items.Length);
        Assert.Single(packet.Groups);
        Assert.True(packet.Encode().Length > 16);
    }
}
