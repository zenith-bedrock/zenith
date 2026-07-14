using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Log;
using Zenith.Network;
using Zenith.Player;
using Zenith.Raknet;

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

        Context = new ServerContext(logger, players, new EventBus(), clock);
        GameLoop = gameLoop;

        RakNetServer = new RakNetServer(port)
        {
            Logger = logger,
            SessionListener = new ZenithSessionListener(Context)
        };
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
