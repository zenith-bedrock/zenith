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
    private readonly List<IGameSystem> _systems = new();

    public GameClock Clock => _clock;

    public GameLoop(GameClock clock, ILogger logger)
    {
        _clock = clock;
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
            foreach (var system in _systems)
            {
                try
                {
                    system.Tick(_clock);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Game system {system.GetType().Name} failed: {ex}");
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
