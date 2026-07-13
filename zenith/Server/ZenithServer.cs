using zenith.Log;
using zenith.Network;
using Zenith.Raknet;

namespace zenith.Server;

/// <summary>
/// Composition root do servidor. Hoje só monta o transporte raknet, mas é aqui que
/// PlayerManager, WorldManager, EventBus e Scheduler vão ser instanciados e conectados
/// conforme forem existindo, em vez de espalhar isso pelo Program.cs.
/// </summary>
class ZenithServer
{
    public RakNetServer RakNetServer { get; }

    public ZenithServer(int port)
    {
        RakNetServer = new RakNetServer(port)
        {
            Logger = new Logger(),
            SessionListener = new ZenithSessionListener()
        };
    }

    public Task StartAsync() => RakNetServer.StartAsync();
}
