using Zenith.Player;
using Zenith.Logging;
using Zenith.Diagnostics;
using System.Diagnostics;

namespace Zenith.Gameplay.Runtime;

/// <summary>
/// Orquestra o ciclo de atualização a 20 TPS (relógio monotônico absoluto, thread dedicada). Avança o <see cref="GameClock"/> e chama
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
    private readonly TickPacingMetrics _pacing;
    private Action<TimeSpan>? _tickObserver;

    /// <summary>How far behind schedule the loop may fall and still catch up by running ticks back to back; beyond it the time is given up.</summary>
    public const int MaxCatchUpTicks = 5;

    public GameClock Clock => _clock;

    public GameLoop(GameClock clock, PlayerManager players, ILogger logger, DiagnosticsRuntime? diagnostics = null, TimingMetric tickTiming = default, TickPacingMetrics pacing = default)
    {
        _clock = clock;
        _players = players;
        _logger = logger;
        _diagnostics = diagnostics;
        _tickTiming = tickTiming;
        _pacing = pacing;
    }

    /// <summary>The registered systems in execution order (composition tests pin it).</summary>
    internal IReadOnlyList<IGameSystem> Systems => _systems;

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

    /// <summary>
    /// Runs the loop on its own dedicated thread until <paramref name="cancellationToken"/> fires, and completes when the thread ends (or
    /// faults: the original exception is the task's). It is not a thread-pool continuation, so no other work can delay a tick's start, and the
    /// pacing is <see cref="TickSchedule"/> over <see cref="PreciseWaiter"/>, not a timer-resolution-bound <c>Task.Delay</c>.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken) => RunAsync(new PreciseWaiter(), cancellationToken, ownsWaiter: true);

    internal Task RunAsync(ITickWaiter waiter, CancellationToken cancellationToken, bool ownsWaiter = false)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Run(waiter, cancellationToken);
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                if (ownsWaiter) waiter.Dispose();
            }
        })
        {
            Name = "zenith-game-loop",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
        return completion.Task;
    }

    private void Run(ITickWaiter waiter, CancellationToken cancellationToken)
    {
        var interval = Stopwatch.Frequency / GameClock.TicksPerSecond;
        var schedule = new TickSchedule(Stopwatch.GetTimestamp(), interval, maxLagTimestampTicks: interval * MaxCatchUpTicks);

        while (!cancellationToken.IsCancellationRequested)
        {
            waiter.WaitUntil(schedule.NextDeadline, cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;

            var woke = Stopwatch.GetTimestamp();
            _diagnostics?.Record(_pacing.Lateness, schedule.Lateness(woke));

            TickOnce();

            var skipped = schedule.Complete(Stopwatch.GetTimestamp());
            if (skipped > 0)
            {
                _diagnostics?.Increment(_pacing.SkippedTicks, skipped);
                _logger.Warning($"Game loop fell {skipped} tick(s) behind its schedule and gave the time up (tick={_clock.CurrentTick}).");
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
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    _logger.Error(
                        $"Fatal game system failure: system={system.GetType().Name} tick={_clock.CurrentTick} " +
                        $"onlinePlayers={_onlineScratch.Count} elapsedInTick={elapsed.TotalMilliseconds:F1}ms: {ex}");
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
