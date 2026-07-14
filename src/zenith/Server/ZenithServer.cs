using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Log;
using Zenith.Network;
using Zenith.Network.Session;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.World;

namespace Zenith.Server;

/// <summary>
/// Composition root do servidor. Monta ServerContext, runtime de gameplay (GameLoop/GameClock)
/// e o transporte raknet. Sistemas de domínio se registram no GameLoop aqui.
/// </summary>
class ZenithServer
{
    private readonly CancellationTokenSource _lifetime = new();

    public RakNetServer RakNetServer { get; }
    public ServerContext Context { get; }
    public GameLoop GameLoop { get; }
    public ServerConfig Config { get; }

    public ZenithServer(ServerConfig config)
    {
        Config = config;
        var logger = new Logger();

        LoginIdentity.RequireChainSignatures = config.Auth.RequireChainSignatures;
        if (!config.Auth.RequireChainSignatures)
        {
            logger.Warning(
                "*** AUTH WARNING: auth.require-chain-signatures is false. " +
                "Any client can claim any username/UUID. Enable before public exposure. ***");
        }

        var players = new PlayerManager();
        var clock = new GameClock();
        var gameLoop = new GameLoop(clock, logger);
        gameLoop.Register(new TimeSyncSystem(players));
        gameLoop.Register(new MovementSystem(players));
        gameLoop.Register(new EquipmentSystem(players));
        gameLoop.Register(new ChatSystem(players));

        var blockPalette = BlockPaletteLoader.FromEmbeddedResource();
        Blocks.Load(blockPalette);
        logger.Info($"Block palette loaded (air={Blocks.Air}, stone={Blocks.Stone}, grass={Blocks.GrassBlock})");

        var itemPalette = ItemPaletteLoader.FromEmbeddedResource();
        // Boot contract: placeable starter blocks + air must exist in item palette.
        _ = itemPalette.Require("minecraft:air");
        _ = itemPalette.Require("minecraft:stone");
        _ = itemPalette.Require("minecraft:grass_block");
        logger.Info($"Item palette loaded ({itemPalette.Count} entries)");

        IChunkStorage storage = CreateChunkStorage(config, logger);
        var world = new World.World(storage);
        gameLoop.Register(new BlockSystem(players, world));
        gameLoop.Register(new InventorySystem(players));
        gameLoop.Register(new ChunkStreamSystem(players, world));

        Context = new ServerContext(logger, players, new EventBus(logger), clock, world, config, blockPalette, itemPalette);
        GameLoop = gameLoop;

        RakNetServer = new RakNetServer(config.Server.Port)
        {
            Logger = logger,
            SessionListener = new ZenithSessionListener(Context),
            MaxConnections = (uint)config.Server.MaxPlayers,
            MaxConnectionsPerAddress = (uint)config.Server.MaxPlayersPerIp,
            Motd = config.Server.Motd,
            SubMotd = config.Server.SubMotd,
            ListGameMode = config.Server.Gamemode,
            ProtocolVersion = ServerIdentity.ProtocolVersion,
            VersionName = ServerIdentity.VersionName
        };
    }

    private static IChunkStorage CreateChunkStorage(ServerConfig config, Zenith.Raknet.Log.ILogger logger)
    {
        var path = config.World.Path?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
        {
            logger.Info("World storage: InMemoryChunkStorage (world.path empty)");
            return new InMemoryChunkStorage();
        }

        try
        {
            Directory.CreateDirectory(path);
            var storage = new LevelDbChunkStorage(path);
            logger.Info($"World storage: LevelDbChunkStorage ({path})");
            return storage;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open LevelDB world at '{path}'. Zenith will not fall back to in-memory storage.",
                ex);
        }
    }

    public async Task StartAsync()
    {
        var token = _lifetime.Token;
        var gameLoopTask = GameLoop.RunAsync(token);
        var raknetTask = RakNetServer.StartAsync();
        await Task.WhenAll(gameLoopTask, raknetTask);
    }

    public Task ShutdownAsync()
    {
        _lifetime.Cancel();
        return RakNetServer.ShutdownAsync();
    }
}
