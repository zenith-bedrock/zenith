namespace Zenith.Diagnostics;

public enum DiagnosticMetricKind : byte
{
    Counter,
    Gauge,
    Timing
}

public enum DiagnosticCategory : byte
{
    Tick,
    Runtime,
    Gameplay,
    Network
}

public readonly record struct CounterMetric(int Index);

public readonly record struct GaugeMetric(int Index);

public readonly record struct TimingMetric(int Index);
