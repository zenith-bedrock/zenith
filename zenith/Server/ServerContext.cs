using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Raknet.Log;
using Zenith.World;

namespace Zenith.Server;

/// <summary>
/// Bundle explícito das dependências centrais do servidor. Passado por construtor pra quem
/// precisar delas, em vez de singleton global.
/// </summary>
class ServerContext
{
    public ILogger Logger { get; }
    public PlayerManager PlayerManager { get; }
    public EventBus EventBus { get; }
    public GameClock Clock { get; }
    public World.World World { get; }
    public ServerConfig Config { get; }

    public ServerContext(
        ILogger logger,
        PlayerManager playerManager,
        EventBus eventBus,
        GameClock clock,
        World.World world,
        ServerConfig config)
    {
        Logger = logger;
        PlayerManager = playerManager;
        EventBus = eventBus;
        Clock = clock;
        World = world;
        Config = config;
    }
}
