using System.Collections.Concurrent;
using System.Net;
using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Network.Packets;
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
            itemPalette);
    }

    public Player.Player AddInGamePlayer(string name)
    {
        var rak = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, _nextPort++),
            Id = _nextPort,
            Server = Transport,
            MTU = 1400
        };
        var session = new NetworkSession(rak, new StubSessionHandler(), Context);
        var player = new Player.Player(name, session, Players.AllocateRuntimeId(), Guid.NewGuid())
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
    [Fact]
    public void BlockSystem_drains_fifo_queue_in_one_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("builder");
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
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 5));

        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, Blocks.Stone, hotbarSlot: -1)));
        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);

        // Place without valid slot must not mutate world or inventory.
        Assert.NotEqual(Blocks.Stone, fx.World.GetBlock(1, 64, 1));
        Assert.Equal(5, player.Inventory.Get(0).Count);
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
        Assert.True(player.Inventory.TrySet(0, Blocks.Stone, 3));
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(9, 64, 9, Blocks.Stone, hotbarSlot: 0)));

        new BlockSystem(fx.Players, fx.World).Tick(fx.Clock);
        Assert.Equal(Blocks.Stone, fx.World.GetBlock(9, 64, 9));
    }
}
