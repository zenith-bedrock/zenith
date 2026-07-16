using System.Collections.Concurrent;
using System.Net;
using Zenith.Event;
using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
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

namespace Zenith.Tests;

file sealed class SilentLogger : ILogger
{
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
}

internal sealed class RecordingRakNetServer : RakNetServer
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
internal sealed class IntentTestFixture
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
            RecipeRegistry.CreateDefault(),
            CreativeCatalog.CreateDefault());
    }

    public InventorySystem CreateInventorySystem() =>
        new(Players, World, Context.Recipes, Context.Creative);

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

    private static void BeginBreakReady(GameClock clock, World.World world, Player.Player player, int x, int y, int z)
    {
        var need = Blocks.BreakTicks(world.GetBlock(x, y, z));
        player.BeginBreak(x, y, z, clock.CurrentTick, need);
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
        var need = Blocks.BreakTicks(world.GetBlock(x, y, z));
        var intent = need > 0
            ? BlockEditIntent.BreakWithDig(x, y, z, player.BreakStartedTick, player.BreakRequiredTicks)
            : BlockEditIntent.Set(x, y, z, Blocks.Air);
        if (need > 0)
            player.ClearBreakTarget();
        Assert.True(player.SubmitBlockEdit(intent));
    }

    [Fact]
    public void MovementSystem_void_triggers_death_not_soft_rescue()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));
        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));

        new MovementSystem(fx.Players).Tick(fx.Clock);

        Assert.True(player.IsDead);
        Assert.Equal(0f, player.Health);
        Assert.Equal("generic", player.DeathCause);
        // Still at void pose until client Respawn — no soft-rescue teleport.
        Assert.Equal(3.5f, player.PositionX);
        Assert.Equal(MovementSystem.VoidRescueY - 1f, player.PositionY);
        Assert.Equal(4.5f, player.PositionZ);
        Assert.Equal(5, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void MovementSystem_respawn_restores_spawn_and_keeps_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("faller");
        Assert.True(player.Inventory.TrySet(0, Blocks.Dirt, 7));
        player.SubmitMovementInput(MovementInputState.From(
            x: 3.5f,
            y: MovementSystem.VoidRescueY - 1f,
            z: 4.5f,
            pitch: 10f,
            yaw: 20f));
        var movement = new MovementSystem(fx.Players);
        movement.Tick(fx.Clock);
        Assert.True(player.IsDead);

        player.SubmitRespawn();
        movement.Tick(fx.Clock);

        Assert.False(player.IsDead);
        Assert.Equal(20f, player.Health);
        Assert.Equal(0f, player.PositionX);
        Assert.Equal(Blocks.FlatSpawnY, player.PositionY);
        Assert.Equal(0f, player.PositionZ);
        Assert.Equal(0f, player.Pitch);
        Assert.Equal(Blocks.Dirt, player.Inventory.GetRuntimeId(0));
        Assert.Equal(7, player.Inventory.Get(0).Count);
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
        QueueReadyBreak(fx.Clock, fx.World, player, 0, 90, 0);
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
        QueueReadyBreak(fx.Clock, fx.World, player, 0, 90, 0);
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(1, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void BlockSystem_rejects_survival_break_without_start_break()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("nostart");
        StandNear(player, 0, 90, 0);
        fx.World.SetBlock(0, 90, 0, Blocks.Stone);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(0, 90, 0, Blocks.Air)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(0, 90, 0));
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
        player.ClearBreakTarget();

        // Simulate Continue on next cell before GameLoop drains the queue (§27).
        player.BeginBreak(1, 90, 0, fx.Clock.CurrentTick, need);
        Assert.True(player.IsBreakTarget(1, 90, 0));

        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

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
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);
        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(0, 90, 0));

        // Proper redig: Start then DigAuthorized Predict.
        player.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need);
        fx.Clock.AdvanceBy(need);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(
            0, 90, 0, player.BreakStartedTick, player.BreakRequiredTicks)));
        player.ClearBreakTarget();
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
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

        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        var gainedA = CountRuntime(a.Inventory, Blocks.Dirt) - beforeA;
        var gainedB = CountRuntime(b.Inventory, Blocks.Dirt) - beforeB;
        Assert.Equal(1, gainedA + gainedB);
        Assert.True(gainedA == 1 ^ gainedB == 1);
    }

    [Fact]
    public void BlockSystem_fans_UpdateBlock_to_joiner_who_Knows_column()
    {
        var fx = new IntentTestFixture();
        var placer = fx.AddInGamePlayer("placer");
        var joiner = fx.AddInGamePlayer("joiner");
        joiner.IsInGame = false;
        joiner.Chunks.RememberMany([(0, 0)]);
        StandNear(placer, 2, 64, 2);
        Assert.True(placer.Inventory.TrySet(0, Blocks.Stone, 5));

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        Assert.True(placer.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 2, Blocks.Stone, hotbarSlot: 0)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);
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

        new ChunkStreamSystem(fx.Players, fx.World).Tick(fx.Clock);
        FlushRaknet(fx.Players);

        Assert.False(player.Chunks.NeedsOverlayResync);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "NeedsOverlayResync must emit UpdateBlock for known-column overlays");
    }

    private static int CountRuntime(PlayerInventory inv, int runtimeId)
    {
        var n = 0;
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
        {
            var s = inv.Get(i);
            if (s.RuntimeId == runtimeId)
                n += s.Count;
        }
        return n;
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
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(5, 64, 5));
        Assert.False(fx.World.Chests.TryGetSlots(5, 64, 5, out _));
        Assert.Equal(Blocks.Chest, player.Inventory.Get(5).RuntimeId);
        Assert.Equal(before + 1, player.Inventory.Get(5).Count);
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
            InventoryStackAction.Transfer(
                0, InventoryContainerMap.ChestBase, 4,
                new WireSlot(InventoryContainerMap.Hotbar, 0),
                new WireSlot(InventoryContainerMap.Chest, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(6, player.Inventory.Get(0).Count);
        Assert.Equal(4, fx.World.Chests.Get(1, 70, 1, 0).Count);
        Assert.Equal(Blocks.Dirt, fx.World.Chests.Get(1, 70, 1, 0).RuntimeId);
    }

    [Fact]
    public void InventorySystem_places_into_craft_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("grid");
        player.InventoryWindowOpen = true;
        Assert.True(player.Inventory.TrySet(0, Blocks.OakLog, 8));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.Transfer(
                0, InventoryContainerMap.CraftUiBase, 1,
                new WireSlot(InventoryContainerMap.CombinedHotbarAndInventory, 0),
                new WireSlot(InventoryContainerMap.CraftingInput, 28))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(7, player.Inventory.Get(0).Count);
        Assert.Equal(Blocks.OakLog, player.CraftUi.GetGrid(0).RuntimeId);
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
    }

    [Fact]
    public void InventorySystem_drop_clears_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropper");
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 10));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.Drop(0, 4, new WireSlot(InventoryContainerMap.Hotbar, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(6, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void InventorySystem_craft_oak_log_to_planks_from_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("crafter");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 2)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(9, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
        Assert.Equal(Blocks.OakLog, player.CraftUi.GetGrid(0).RuntimeId);
        Assert.Equal(4, player.CraftUi.Result.Count);
        Assert.Equal(Blocks.OakPlanks, player.CraftUi.Result.RuntimeId);
    }

    [Fact]
    public void InventorySystem_craft_then_take_result_to_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("takeout");
        player.InventoryWindowOpen = true;
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 1)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(10, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, 4,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Cursor.RuntimeId);
        Assert.Equal(4, player.Inventory.Cursor.Count);
    }

    [Fact]
    public void InventorySystem_craft_chain_planks_then_chest_take()
    {
        // S35: log→planks take, place planks, chest craft, take chest (sequential intents).
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chaincraft");
        player.InventoryWindowOpen = true;
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySet(0, Blocks.OakLog, 2));

        var sys = fx.CreateInventorySystem();

        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 1)));
        Assert.True(player.Inventory.TrySet(0, Blocks.OakLog, 1));
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(20, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 1, 4)
        ])));
        sys.Tick(fx.Clock);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(1).RuntimeId);
        Assert.Equal(4, player.Inventory.Get(1).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);

        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 1)));
        Assert.True(player.Inventory.TrySet(0, Blocks.Air, 0));
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(21, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 2, 4)
        ])));
        sys.Tick(fx.Clock);
        Assert.Equal(4, player.Inventory.Get(2).Count);
        Assert.True(player.CraftUi.Result.IsEmpty);

        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakPlanks, 2)));
        Assert.True(player.CraftUi.TrySetGrid(1, new InventorySlot(Blocks.OakPlanks, 2)));
        Assert.True(player.CraftUi.TrySetGrid(2, new InventorySlot(Blocks.OakPlanks, 2)));
        Assert.True(player.CraftUi.TrySetGrid(3, new InventorySlot(Blocks.OakPlanks, 2)));
        Assert.True(player.Inventory.TrySet(1, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySet(2, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(22, [
            InventoryStackAction.Craft(RecipeRegistry.OakPlanksToChest),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 3, 1,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 3))
        ])));
        sys.Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.Chest, player.Inventory.Get(3).RuntimeId);
        Assert.Equal(1, player.Inventory.Get(3).Count);
    }

    [Fact]
    public void InventorySystem_craft_refuses_while_result_occupied()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stuckout");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 2)));

        var sys = fx.CreateInventorySystem();
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(30, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks)
        ])));
        sys.Tick(fx.Clock);
        Assert.Equal(Blocks.OakPlanks, player.CraftUi.Result.RuntimeId);

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(31, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks)
        ])));
        sys.Tick(fx.Clock);

        Assert.Equal(Blocks.OakPlanks, player.CraftUi.Result.RuntimeId);
        Assert.Equal(1, player.CraftUi.GetGrid(0).Count);
    }

    [Fact]
    public void InventorySystem_craft_times_two_then_place_result_to_bag()
    {
        // Shift-click craft output: CraftRecipe(times=2) + Place 8 planks to bag.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shiftcraft");
        player.InventoryWindowOpen = true;
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 2)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(12, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks, craftTimes: 2),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 9, 8,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 9))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.GetGrid(0).IsEmpty);
        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(9).RuntimeId);
        Assert.Equal(8, player.Inventory.Get(9).Count);
    }

    [Fact]
    public void InventorySystem_craft_times_zero_treated_as_one()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("times0");
        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 1)));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(13, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks, craftTimes: 0),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(4, player.CraftUi.Result.Count);
        Assert.True(player.CraftUi.GetGrid(0).IsEmpty);
    }

    [Fact]
    public void InventorySystem_craft_times_clamped_by_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("clamp");
        Assert.True(player.CraftUi.TrySetGrid(0, new InventorySlot(Blocks.OakLog, 1)));

        // Request 5 crafts but only 1 log → clamp to 1, not fail.
        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(14, [
            InventoryStackAction.Craft(RecipeRegistry.OakLogToPlanks, craftTimes: 5),
            InventoryStackAction.CreateOutput()
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(4, player.CraftUi.Result.Count);
        Assert.True(player.CraftUi.GetGrid(0).IsEmpty);
    }

    [Fact]
    public void InventorySystem_storage_slot_to_cursor_via_container_29()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("storage");
        player.InventoryWindowOpen = true;
        Assert.True(player.Inventory.TrySet(9, Blocks.Dirt, 8));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(11, [
            InventoryStackAction.Transfer(
                9, PlayerInventory.CursorSlot, 8,
                new WireSlot(InventoryContainerMap.Inventory, 9),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.Inventory.Get(9).IsEmpty);
        Assert.Equal(Blocks.Dirt, player.Inventory.Cursor.RuntimeId);
        Assert.Equal(8, player.Inventory.Cursor.Count);
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
        var packet = InventoryProtocol.BuildCreativeContent(CreativeCatalog.CreateDefault(), palette);
        Assert.Equal(7, packet.Items.Length);
        Assert.Equal(CreativeCatalog.Stone, packet.Items[0].CreativeItemNetworkId);
        Assert.Single(packet.Groups);
        Assert.True(packet.Encode().Length > 16);
    }

    [Fact]
    public void InventorySystem_craft_creative_places_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creator", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(1, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, PlayerInventory.MaxStack,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Cursor, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.Stone, player.Inventory.Cursor.RuntimeId);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }

    [Fact]
    public void InventorySystem_craft_creative_shift_to_bag()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shiftcreate", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(2, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 0, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.Equal(Blocks.Stone, player.Inventory.Get(0).RuntimeId);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void InventorySystem_craft_creative_merges_same_item_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("merges", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySet(PlayerInventory.CursorSlot, Blocks.Stone, 32));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(3, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, 32)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.Equal(Blocks.Stone, player.Inventory.Cursor.RuntimeId);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
        Assert.Equal(32, player.CraftUi.Result.Count);
    }

    [Fact]
    public void InventorySystem_craft_creative_rejects_different_item_on_cursor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("clash", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySet(PlayerInventory.CursorSlot, Blocks.Dirt, 1));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(4, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(Blocks.Dirt, player.Inventory.Cursor.RuntimeId);
        Assert.Equal(1, player.Inventory.Cursor.Count);
    }

    [Fact]
    public void InventorySystem_craft_creative_rejects_cursor_overflow()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fullcursor", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySet(PlayerInventory.CursorSlot, Blocks.Stone, PlayerInventory.MaxStack));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(7, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, 1)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.Equal(PlayerInventory.MaxStack, player.Inventory.Cursor.Count);
    }

    [Fact]
    public void InventorySystem_craft_creative_drop_from_result()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropcreate", GameMode.Creative);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(5, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Drop(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.MaxStack,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }

    [Fact]
    public void InventorySystem_craft_creative_survival_rejects()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("survivor");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySet(i, Blocks.Air, 0));

        Assert.True(player.SubmitInventoryStack(InventoryStackIntent.Create(6, [
            InventoryStackAction.CraftCreative(CreativeCatalog.Stone),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, PlayerInventory.CursorSlot, PlayerInventory.MaxStack)
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock);

        Assert.True(player.CraftUi.Result.IsEmpty);
        Assert.True(player.Inventory.Cursor.IsEmpty);
        Assert.True(player.Inventory.Get(0).IsEmpty);
    }

    [Fact]
    public void CreativeContent_empty_icon_is_varint_air_not_item_instance_new()
    {
        // Empty ItemStack = single VarInt 0 after group header — not ItemInstanceNew short LE.
        var emptyPk = new CreativeContentPacket
        {
            Groups = [new CreativeGroupEntry(1, "", NetworkItemStack.Empty)],
            Items = []
        };
        var bytes = emptyPk.Encode().ToArray();
        // packet id UVInt + groups UVInt(1) + category i32 + name UVInt(0) + Item VarInt(0) + items UVInt(0)
        Assert.True(bytes.Length < 20);

        var writer = new BinaryStream();
        NetworkItemStack.Empty.WriteItem(ref writer);
        var air = writer.GetBufferDisposing().ToArray();
        Assert.Equal(new byte[] { 0 }, air); // VarInt 0
    }
}
