using Zenith.Event;
using Zenith.Log;
using Zenith.Network;
using Zenith.Player;
using Zenith.Raknet;

namespace Zenith.Server;

/// <summary>
/// Composition root do servidor. Monta o ServerContext (Logger, PlayerManager, EventBus) e o
/// transporte raknet, e é aqui que WorldManager e Scheduler vão entrar no Context conforme
/// forem existindo, em vez de espalhar isso pelo Program.cs.
/// </summary>
class ZenithServer
{
    public RakNetServer RakNetServer { get; }
    public ServerContext Context { get; }

    public ZenithServer(int port)
    {
        var logger = new Logger();
        Context = new ServerContext(logger, new PlayerManager(), new EventBus());

        RakNetServer = new RakNetServer(port)
        {
            Logger = logger,
            SessionListener = new ZenithSessionListener(Context)
        };
    }

    public Task StartAsync() => RakNetServer.StartAsync();
}
