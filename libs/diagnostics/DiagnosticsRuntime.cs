using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Zenith.Diagnostics;

/// <summary>
/// Fixed-layout, lock-free runtime diagnostics. Recording is allocation-free and uses only
/// atomic array operations, so gameplay and transport writers can share one instance safely.
/// Snapshots intentionally allocate their own immutable view for an external reader/exporter.
/// </summary>
public sealed class DiagnosticsRuntime
{
    private readonly MetricDefinition[] _definitions;
    private readonly long[] _values;
    private readonly long[] _counts;
    private readonly long[] _totals;
    private readonly long[] _maximums;

    internal DiagnosticsRuntime(MetricDefinition[] definitions)
    {
        _definitions = definitions;
        _values = new long[definitions.Length];
        _counts = new long[definitions.Length];
        _totals = new long[definitions.Length];
        _maximums = new long[definitions.Length];
    }

    public void Increment(CounterMetric metric, long value = 1) =>
        Interlocked.Add(ref _values[Validate(metric.Index, DiagnosticMetricKind.Counter)], value);

    public void Set(GaugeMetric metric, long value) =>
        Interlocked.Exchange(ref _values[Validate(metric.Index, DiagnosticMetricKind.Gauge)], value);

    public TimingScope Begin(TimingMetric metric) =>
        new(this, Validate(metric.Index, DiagnosticMetricKind.Timing), Stopwatch.GetTimestamp());

    public void Record(TimingMetric metric, long elapsedStopwatchTicks) =>
        RecordValidated(Validate(metric.Index, DiagnosticMetricKind.Timing), elapsedStopwatchTicks);

    public DiagnosticsSnapshot CaptureSnapshot() => CaptureSnapshot(DateTimeOffset.UtcNow);

    public DiagnosticsSnapshot CaptureSnapshot(DateTimeOffset capturedAt)
    {
        var values = new long[_definitions.Length];
        var counts = new long[_definitions.Length];
        var totals = new long[_definitions.Length];
        var maximums = new long[_definitions.Length];
        CopyRawValues(values, counts, totals, maximums);
        return CreateSnapshot(capturedAt, values, counts, totals, maximums);
    }

    internal void RecordValidated(int index, long elapsedStopwatchTicks)
    {
        if (elapsedStopwatchTicks < 0) return;
        Interlocked.Exchange(ref _values[index], elapsedStopwatchTicks);
        Interlocked.Increment(ref _counts[index]);
        Interlocked.Add(ref _totals[index], elapsedStopwatchTicks);

        long observed;
        while (elapsedStopwatchTicks > (observed = Interlocked.Read(ref _maximums[index])) &&
               Interlocked.CompareExchange(ref _maximums[index], elapsedStopwatchTicks, observed) != observed)
        {
        }
    }

    internal int MetricCount => _definitions.Length;

    internal void CopyRawValues(long[] values, long[] counts, long[] totals, long[] maximums)
    {
        for (var index = 0; index < _definitions.Length; index++)
        {
            values[index] = Interlocked.Read(ref _values[index]);
            counts[index] = Interlocked.Read(ref _counts[index]);
            totals[index] = Interlocked.Read(ref _totals[index]);
            maximums[index] = Interlocked.Read(ref _maximums[index]);
        }
    }

    internal DiagnosticsSnapshot CreateSnapshot(DateTimeOffset capturedAt, long[] values, long[] counts, long[] totals, long[] maximums)
    {
        var metrics = new DiagnosticsMetricSnapshot[_definitions.Length];
        for (var index = 0; index < _definitions.Length; index++)
        {
            var definition = _definitions[index];
            var parent = definition.ParentIndex < 0 ? null : _definitions[definition.ParentIndex].Name;
            metrics[index] = new DiagnosticsMetricSnapshot(
                definition.Name, definition.Kind, definition.Category, parent,
                values[index], counts[index], totals[index], maximums[index]);
        }
        return new DiagnosticsSnapshot(capturedAt, Stopwatch.Frequency, metrics);
    }

    private int Validate(int index, DiagnosticMetricKind expected)
    {
        if ((uint)index >= (uint)_definitions.Length || _definitions[index].Kind != expected)
            throw new ArgumentOutOfRangeException(nameof(index), "Metric handle does not belong to this diagnostics layout.");
        return index;
    }
}

public readonly struct TimingScope : IDisposable
{
    private readonly DiagnosticsRuntime? _runtime;
    private readonly int _index;
    private readonly long _started;

    internal TimingScope(DiagnosticsRuntime runtime, int index, long started)
    {
        _runtime = runtime;
        _index = index;
        _started = started;
    }

    public void Dispose() => _runtime?.RecordValidated(_index, Stopwatch.GetTimestamp() - _started);
}

public sealed record DiagnosticsSnapshot(DateTimeOffset CapturedAt, long StopwatchFrequency, DiagnosticsMetricSnapshot[] Metrics)
{
    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = indented });

    public string ToConsole()
    {
        var output = new StringBuilder();
        output.Append("diagnostics @ ").Append(CapturedAt.ToString("O")).AppendLine();
        foreach (var metric in Metrics)
        {
            output.Append(metric.Name).Append('=').Append(metric.Value);
            if (metric.Kind == DiagnosticMetricKind.Timing)
            {
                var average = metric.Count == 0 ? 0d : metric.TotalStopwatchTicks * 1000d / StopwatchFrequency / metric.Count;
                output.Append(" ticks=").Append(metric.Count)
                    .Append(" avgMs=").Append(average.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
            output.AppendLine();
        }
        return output.ToString();
    }
}

public readonly record struct DiagnosticsMetricSnapshot(
    string Name,
    DiagnosticMetricKind Kind,
    DiagnosticCategory Category,
    string? Parent,
    long Value,
    long Count,
    long TotalStopwatchTicks,
    long MaximumStopwatchTicks);
