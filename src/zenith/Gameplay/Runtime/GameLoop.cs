using Zenith.Player;
using Zenith.Raknet.Log;
using Zenith.Diagnostics;
using System.Diagnostics;

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
    private readonly List<TimingMetric?> _systemTimings = new();
    private readonly List<Player.Player> _onlineScratch = new();
    private readonly DiagnosticsRuntime? _diagnostics;
    private readonly TimingMetric _tickTiming;
    private Action<TimeSpan>? _tickObserver;

    public GameClock Clock => _clock;

    public GameLoop(GameClock clock, PlayerManager players, ILogger logger, DiagnosticsRuntime? diagnostics = null, TimingMetric tickTiming = default)
    {
        _clock = clock;
        _players = players;
        _logger = logger;
        _diagnostics = diagnostics;
        _tickTiming = tickTiming;
    }

    /// <summary>Ordem de registro = ordem de execução.</summary>
    public void Register(IGameSystem system)
    {
        _systems.Add(system);
        _systemTimings.Add(null);
    }

    public void Register(IGameSystem system, TimingMetric timing)
    {
        _systems.Add(system);
        _systemTimings.Add(timing);
    }

    /// <summary>Composition-root-only operational hook; it observes completed ticks and owns no gameplay state.</summary>
    internal void SetTickObserver(Action<TimeSpan> observer) => _tickObserver = observer;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / GameClock.TicksPerSecond);
        var loopStart = TimeSpan.FromMilliseconds(Environment.TickCount64);
        ulong tickIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            TickOnce();

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

    /// <summary>
    /// Executes one complete authoritative gameplay tick. Internal tooling and tests use this
    /// to exercise the same system ordering as the server without a wall-clock delay.
    /// </summary>
    internal void TickOnce()
    {
        var started = Stopwatch.GetTimestamp();
        var tickScope = _diagnostics is null ? default : _diagnostics.Begin(_tickTiming);
        try
        {
            _clock.Advance();
            _players.FillOnline(_onlineScratch);
            for (var index = 0; index < _systems.Count; index++)
            {
                var system = _systems[index];
                var systemScope = _systemTimings[index] is { } timing && _diagnostics is not null
                    ? _diagnostics.Begin(timing)
                    : default;
                try
                {
                    system.Tick(_clock, _onlineScratch);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fatal game system failure in {system.GetType().Name}: {ex}");
                    throw;
                }
                finally { systemScope.Dispose(); }
            }
        }
        finally
        {
            tickScope.Dispose();
            _tickObserver?.Invoke(Stopwatch.GetElapsedTime(started));
        }
    }
}
