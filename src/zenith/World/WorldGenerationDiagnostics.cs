using System.Threading;
using Zenith.Diagnostics;

namespace Zenith.World;

/// <summary>
/// Fixed-layout world-generation observations supplied by the server composition root.
/// Recording is deliberately column-scoped; block loops must not call diagnostics directly.
/// </summary>
sealed class WorldGenerationDiagnostics
{
    private readonly DiagnosticsRuntime _runtime;
    private readonly CounterMetric _columnsRequested;
    private readonly CounterMetric _columnsGenerated;
    private readonly CounterMetric _columnsLoaded;
    private readonly CounterMetric _columnsFailed;
    private readonly CounterMetric _columnsCoalesced;
    private readonly CounterMetric _columnsCacheHit;
    private readonly CounterMetric _columnsDropped;
    private readonly GaugeMetric _columnsInFlight;
    private readonly TimingMetric _column;
    private readonly TimingMetric _surface;
    private readonly TimingMetric _caves;
    private readonly TimingMetric _features;
    private readonly TimingMetric _payload;
    private readonly TimingMetric _sampling;
    private readonly TimingMetric _encode;
    private readonly TimingMetric _preSpawnLoad;
    private readonly TimingMetric _preSpawnPublish;
    private readonly CounterMetric _preSpawnColumns;
    private readonly CounterMetric _preSpawnBytes;
    private readonly TimingMetric _queueWait;
    private readonly CounterMetric _queueBackpressure;
    private readonly GaugeMetric _queueDepth;
    private readonly GaugeMetric _queuePeak;
    private long _queuePeakValue;
    private int _inFlight;

    internal WorldGenerationDiagnostics(
        DiagnosticsRuntime runtime,
        CounterMetric columnsRequested,
        CounterMetric columnsGenerated,
        CounterMetric columnsLoaded,
        CounterMetric columnsFailed,
        CounterMetric columnsCoalesced,
        CounterMetric columnsCacheHit,
        CounterMetric columnsDropped,
        GaugeMetric columnsInFlight,
        TimingMetric column,
        TimingMetric surface,
        TimingMetric caves,
        TimingMetric features,
        TimingMetric payload,
        TimingMetric sampling,
        TimingMetric encode,
        TimingMetric preSpawnLoad,
        TimingMetric preSpawnPublish,
        CounterMetric preSpawnColumns,
        CounterMetric preSpawnBytes,
        TimingMetric queueWait,
        CounterMetric queueBackpressure,
        GaugeMetric queueDepth,
        GaugeMetric queuePeak)
    {
        _runtime = runtime;
        _columnsRequested = columnsRequested;
        _columnsGenerated = columnsGenerated;
        _columnsLoaded = columnsLoaded;
        _columnsFailed = columnsFailed;
        _columnsCoalesced = columnsCoalesced;
        _columnsCacheHit = columnsCacheHit;
        _columnsDropped = columnsDropped;
        _columnsInFlight = columnsInFlight;
        _column = column;
        _surface = surface;
        _caves = caves;
        _features = features;
        _payload = payload;
        _sampling = sampling;
        _encode = encode;
        _preSpawnLoad = preSpawnLoad;
        _preSpawnPublish = preSpawnPublish;
        _preSpawnColumns = preSpawnColumns;
        _preSpawnBytes = preSpawnBytes;
        _queueWait = queueWait;
        _queueBackpressure = queueBackpressure;
        _queueDepth = queueDepth;
        _queuePeak = queuePeak;
    }

    internal ColumnScope BeginColumn()
    {
        _runtime.Increment(_columnsRequested);
        _runtime.Set(_columnsInFlight, Interlocked.Increment(ref _inFlight));
        return new ColumnScope(this, _runtime.Begin(_column));
    }

    internal void RecordGenerated() => _runtime.Increment(_columnsGenerated);

    internal void RecordLoaded() => _runtime.Increment(_columnsLoaded);

    internal void RecordFailed() => _runtime.Increment(_columnsFailed);

    internal void RecordCoalesced() => _runtime.Increment(_columnsCoalesced);

    internal void RecordCacheHit() => _runtime.Increment(_columnsCacheHit);

    internal void RecordDropped() => _runtime.Increment(_columnsDropped);

    internal TimingScope BeginQueueWait() => _runtime.Begin(_queueWait);

    internal void RecordQueueBackpressure() => _runtime.Increment(_queueBackpressure);

    internal void RecordQueueDepth(int depth)
    {
        depth = Math.Max(depth, 0);
        _runtime.Set(_queueDepth, depth);

        var observed = Interlocked.Read(ref _queuePeakValue);
        while (depth > observed)
        {
            var previous = Interlocked.CompareExchange(ref _queuePeakValue, depth, observed);
            if (previous == observed)
            {
                _runtime.Set(_queuePeak, depth);
                break;
            }

            observed = previous;
        }
    }

    internal TimingScope BeginPreSpawnLoad() => _runtime.Begin(_preSpawnLoad);

    internal TimingScope BeginSurface() => _runtime.Begin(_surface);

    internal TimingScope BeginCaves() => _runtime.Begin(_caves);

    internal TimingScope BeginFeatures() => _runtime.Begin(_features);

    internal TimingScope BeginPayload() => _runtime.Begin(_payload);

    internal TimingScope BeginSampling() => _runtime.Begin(_sampling);

    internal TimingScope BeginEncode() => _runtime.Begin(_encode);

    internal TimingScope BeginPreSpawnPublish() => _runtime.Begin(_preSpawnPublish);

    internal void RecordPreSpawnPublished(int columns, long bytes)
    {
        if (columns > 0) _runtime.Increment(_preSpawnColumns, columns);
        if (bytes > 0) _runtime.Increment(_preSpawnBytes, bytes);
    }

    private void EndColumn(TimingScope timing)
    {
        timing.Dispose();
        _runtime.Set(_columnsInFlight, Interlocked.Decrement(ref _inFlight));
    }

    internal readonly struct ColumnScope : IDisposable
    {
        private readonly WorldGenerationDiagnostics? _owner;
        private readonly TimingScope _timing;

        internal ColumnScope(WorldGenerationDiagnostics owner, TimingScope timing)
        {
            _owner = owner;
            _timing = timing;
        }

        public void Dispose() => _owner?.EndColumn(_timing);
    }
}
