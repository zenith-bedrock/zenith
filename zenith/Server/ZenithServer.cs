using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Log;
using Zenith.Network;
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

    public ZenithServer(int port)
    {
        var logger = new Logger();
        var players = new PlayerManager();
        var clock = new GameClock();
        var gameLoop = new GameLoop(clock, logger);
        gameLoop.Register(new TimeSyncSystem(players));
        gameLoop.Register(new MovementSystem(players));

        IChunkStorage storage = CreateChunkStorage(logger);
        var world = new World.World(storage);
        gameLoop.Register(new BlockSystem(players, world));

        Context = new ServerContext(logger, players, new EventBus(), clock, world);
        GameLoop = gameLoop;

        RakNetServer = new RakNetServer(port)
        {
            Logger = logger,
            SessionListener = new ZenithSessionListener(Context)
        };
    }

    /// <summary>
    /// In-memory por padrão. LevelDB quando ZENITH_WORLD_PATH aponta pra um diretório.
    /// </summary>
    private static IChunkStorage CreateChunkStorage(Zenith.Raknet.Log.ILogger logger)
    {
        var path = Environment.GetEnvironmentVariable("ZENITH_WORLD_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.Info("World storage: InMemoryChunkStorage");
            return new InMemoryChunkStorage();
        }

        Directory.CreateDirectory(path);
        logger.Info($"World storage: LevelDbChunkStorage ({path})");
        return new LevelDbChunkStorage(path);
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
