using Zenith.Player;
using Zenith.Raknet.Log;

namespace Zenith.Gameplay.Runtime;

/// <summary>
/// Orquestra o ciclo de atualização a 20 TPS. Avança o <see cref="GameClock"/> e chama
/// sistemas na ordem de <see cref="Register"/>. Sem regras de gameplay.
/// </summary>
sealed class GameLoop
{
    private readonly GameClock _clock;
    private readonly ILogger _logger;
    private readonly PlayerManager _players;
    private readonly List<IGameSystem> _systems = new();
    private readonly List<Player.Player> _onlineScratch = new();

    public GameClock Clock => _clock;

    public GameLoop(GameClock clock, PlayerManager players, ILogger logger)
    {
        _clock = clock;
        _players = players;
        _logger = logger;
    }

    /// <summary>Ordem de registro = ordem de execução.</summary>
    public void Register(IGameSystem system) => _systems.Add(system);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / GameClock.TicksPerSecond);
        var loopStart = TimeSpan.FromMilliseconds(Environment.TickCount64);
        ulong tickIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            _clock.Advance();
            _players.FillOnline(_onlineScratch);
            foreach (var system in _systems)
            {
                try
                {
                    system.Tick(_clock, _onlineScratch);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fatal game system failure in {system.GetType().Name}: {ex}");
                    throw;
                }
            }

            tickIndex++;
            var deadline = loopStart + interval * tickIndex;
            var now = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var delay = deadline - now;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
