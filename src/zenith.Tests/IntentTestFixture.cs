using System.Collections.Concurrent;
using System.Net;
using Zenith.Event;
using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Raknet.Log;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.Session;
using Zenith.Session.Handler;
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
    public ConcurrentQueue<(IPEndPoint EndPoint, byte[] Datagram)> CapturedByEndpoint { get; } = new();

    public RecordingRakNetServer() : base(port: 0) { }

    public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer)
    {
        var datagram = buffer.ToArray();
        Captured.Enqueue(datagram);
        CapturedByEndpoint.Enqueue((endPoint, datagram));
    }
}

file sealed class StubSessionHandler : ISessionHandler
{
    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream) =>
        false;
}

/// <summary>
/// Monta Player + NetworkSession + world sem boot completo do ZenithServer. Shared by ~30 test
/// files (Phase XIII.1) — moved out of IntentContractTests.cs, which is one of its many consumers,
/// not its home; a harness this widely shared needs to be found on its own.
/// </summary>
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

    public Player.Player AddInGamePlayer(string name, GameMode gameMode = GameMode.Survival) =>
        AddPlayer(name, gameMode, isInGame: true);

    /// <summary>Registered in <see cref="Players"/> but not yet spawned — mirrors the window between
    /// login accept and <c>InGameSessionHandler</c> setting <c>IsInGame</c> (e.g. mid resource-pack).</summary>
    public Player.Player AddPlayer(string name, GameMode gameMode = GameMode.Survival, bool isInGame = false)
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
            IsInGame = isInGame
        };
        session.Player = player;
        Assert.True(Players.TryAdd(player));
        return player;
    }
}
