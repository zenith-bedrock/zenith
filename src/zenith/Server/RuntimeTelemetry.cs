using System.Diagnostics;
using System.Runtime;
using Zenith.Network;
using Zenith.Logging;

namespace Zenith.Server;

/// <summary>Small server-owned operational snapshot; logs health, it does not expose a metrics platform.</summary>
sealed class RuntimeTelemetry
{
    private const double TickBudgetMs = 50d;
    private const int LogEveryTicks = 20 * 30;
    private readonly ILogger _logger;
    private long _ticks;
    private long _overBudget;
    private long _lastTickMicroseconds;
    private long _lastFlushMilliseconds;
    private TimeSpan _gcPauseAtLastLog = GC.GetTotalPauseDuration();

    public RuntimeTelemetry(ILogger logger) => _logger = logger;

    /// <summary>
    /// The GC configuration this process actually runs with (workstation/server, concurrent, latency mode), logged once at start so a
    /// pause measurement can be read against the setting that produced it.
    /// </summary>
    public static string DescribeGc() =>
        $"gc: {(GCSettings.IsServerGC ? "server" : "workstation")} concurrent={AppContext.GetData("System.GC.Concurrent") ?? "default(true)"} " +
        $"latency={GCSettings.LatencyMode} tieredPGO={AppContext.GetData("System.Runtime.TieredPGO") ?? "default"}";

    public void RecordTick(TimeSpan elapsed, int players, int actors, TransportStatistics transport)
    {
        var ticks = Interlocked.Increment(ref _ticks);
        var micros = (long)(elapsed.TotalMilliseconds * 1_000d);
        Interlocked.Exchange(ref _lastTickMicroseconds, micros);
        if (elapsed.TotalMilliseconds > TickBudgetMs) Interlocked.Increment(ref _overBudget);
        if (ticks % LogEveryTicks != 0) return;

        // Pause time inside this log window: the GC cost the tick actually felt, which collection counts alone do not show.
        var pauseNow = GC.GetTotalPauseDuration();
        var pauseWindow = pauseNow - _gcPauseAtLastLog;
        _gcPauseAtLastLog = pauseNow;

        _logger.Info($"runtime: tick={micros / 1000d:F3}ms overBudget={Interlocked.Read(ref _overBudget)} " +
                     $"players={players} actors={actors} connections={transport.Connections} " +
                     $"out={transport.SentDatagrams}/{transport.SentBytes}B in={transport.ReceivedDatagrams}/{transport.ReceivedBytes}B " +
                     $"gc={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} gcPause={pauseWindow.TotalMilliseconds:F1}ms/30s " +
                     $"managed={GC.GetTotalMemory(forceFullCollection: false)}B flush={Interlocked.Read(ref _lastFlushMilliseconds)}ms");
    }

    public void RecordFlush(TimeSpan elapsed) => Interlocked.Exchange(ref _lastFlushMilliseconds, (long)elapsed.TotalMilliseconds);

    internal RuntimeTelemetrySnapshot Snapshot => new(
        Interlocked.Read(ref _ticks), Interlocked.Read(ref _overBudget),
        Interlocked.Read(ref _lastTickMicroseconds) / 1000d, Interlocked.Read(ref _lastFlushMilliseconds));
}

internal readonly record struct RuntimeTelemetrySnapshot(long Ticks, long OverBudgetTicks, double LastTickMilliseconds, long LastFlushMilliseconds);
