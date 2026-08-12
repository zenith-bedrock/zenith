using BenchmarkDotNet.Attributes;
using Zenith.Diagnostics;

namespace Zenith.Benchmarks;

/// <summary>Run with <c>--filter *DiagnosticsOverhead*</c> to compare the hot recording path.</summary>
[MemoryDiagnoser]
public class DiagnosticsOverheadBenchmarks
{
    private DiagnosticsRuntime _diagnostics = null!;
    private CounterMetric _packets;
    private GaugeMetric _players;
    private TimingMetric _tick;
    private DiagnosticsSnapshot _baseline = null!;
    private long _baselineValue;

    [GlobalSetup]
    public void Setup()
    {
        var builder = new DiagnosticsBuilder();
        _tick = builder.Timing("tick");
        _players = builder.Gauge("gameplay.players");
        _packets = builder.Counter("network.packets.sent");
        _diagnostics = builder.Build();
        _diagnostics.Increment(_packets, 100);
        _diagnostics.Set(_players, 10);
        _diagnostics.Record(_tick, 10);
        _baseline = _diagnostics.CaptureSnapshot();
    }

    [Benchmark(Baseline = true)]
    public long TickWithoutDiagnostics()
    {
        _baselineValue++;
        return _baselineValue;
    }

    [Benchmark] public void Counter() => _diagnostics.Increment(_packets);

    [Benchmark] public void Gauge() => _diagnostics.Set(_players, 1);

    [Benchmark] public void Timing() => _diagnostics.Record(_tick, 1);

    [Benchmark]
    public void TickWithDiagnostics()
    {
        Counter();
        Gauge();
        Timing();
    }

    [Benchmark]
    public DiagnosticsSnapshot CaptureSnapshot() => _diagnostics.CaptureSnapshot();

    [Benchmark]
    public DiagnosticsComparison CompareSnapshot() =>
        DiagnosticsComparison.Create(_baseline, _diagnostics.CaptureSnapshot());
}
