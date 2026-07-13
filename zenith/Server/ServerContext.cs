using Zenith.Event;
using Zenith.Player;
using Zenith.Raknet.Log;

namespace Zenith.Server;

/// <summary>
/// Bundle explícito das dependências centrais do servidor (Logger, PlayerManager, EventBus, ...).
/// Passado por construtor pra quem precisar delas, em vez de:
///   a) empilhar mais um parâmetro no construtor de cada classe toda vez que um sistema novo
///      central aparece (WorldManager, Scheduler, ...), ou
///   b) virar um <c>Server.getInstance()</c> global estilo PocketMine.
///
/// ServerContext em si não tem lógica nenhuma, é só o conjunto de "coisas que praticamente
/// tudo no lado do jogo precisa". Sistemas que só precisam de uma peça específica (ex: só
/// PlayerManager) continuam podendo pedir só aquela peça no construtor deles; ServerContext
/// existe pra quem precisa de várias ao mesmo tempo.
/// </summary>
class ServerContext
{
    public ILogger Logger { get; }
    public PlayerManager PlayerManager { get; }
    public EventBus EventBus { get; }

    public ServerContext(ILogger logger, PlayerManager playerManager, EventBus eventBus)
    {
        Logger = logger;
        PlayerManager = playerManager;
        EventBus = eventBus;
    }
}
