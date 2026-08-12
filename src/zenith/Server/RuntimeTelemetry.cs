using System.Diagnostics;
using Zenith.Raknet;
using Zenith.Raknet.Log;

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

    public RuntimeTelemetry(ILogger logger) => _logger = logger;

    public void RecordTick(TimeSpan elapsed, int players, int actors, RakNetServer raknet)
    {
        var ticks = Interlocked.Increment(ref _ticks);
        var micros = (long)(elapsed.TotalMilliseconds * 1_000d);
        Interlocked.Exchange(ref _lastTickMicroseconds, micros);
        if (elapsed.TotalMilliseconds > TickBudgetMs) Interlocked.Increment(ref _overBudget);
        if (ticks % LogEveryTicks != 0) return;

        _logger.Info($"runtime: tick={micros / 1000d:F3}ms overBudget={Interlocked.Read(ref _overBudget)} " +
                     $"players={players} actors={actors} raknetSessions={raknet.ConnectionCount} " +
                     $"out={raknet.SentDatagrams}/{raknet.SentBytes}B in={raknet.ReceivedDatagrams}/{raknet.ReceivedBytes}B " +
                     $"gc={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} " +
                     $"managed={GC.GetTotalMemory(forceFullCollection: false)}B flush={Interlocked.Read(ref _lastFlushMilliseconds)}ms");
    }

    public void RecordFlush(TimeSpan elapsed) => Interlocked.Exchange(ref _lastFlushMilliseconds, (long)elapsed.TotalMilliseconds);

    internal RuntimeTelemetrySnapshot Snapshot => new(
        Interlocked.Read(ref _ticks), Interlocked.Read(ref _overBudget),
        Interlocked.Read(ref _lastTickMicroseconds) / 1000d, Interlocked.Read(ref _lastFlushMilliseconds));
}

internal readonly record struct RuntimeTelemetrySnapshot(long Ticks, long OverBudgetTicks, double LastTickMilliseconds, long LastFlushMilliseconds);
