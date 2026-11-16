using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private readonly Slot[] _slots;
    private readonly long[] _histograms;

    /// <summary>
    /// One metric's mutable state, padded to 128 bytes so two metrics never share a cache line (nor the adjacent line the hardware
    /// prefetches with it): the tick thread and the transport threads write different metrics at the same time, and without the
    /// padding those writes invalidate each other's lines. It also keeps all four fields of one timing on a single line.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 128)]
    private struct Slot
    {
        public long Value;
        public long Count;
        public long Total;
        public long Maximum;
    }

    internal DiagnosticsRuntime(MetricDefinition[] definitions, int timingCount)
    {
        _histograms = new long[timingCount * DurationHistogram.BucketCount];
        _definitions = definitions;
        _slots = new Slot[definitions.Length];
    }

    public void Increment(CounterMetric metric, long value = 1) =>
        Interlocked.Add(ref _slots[Validate(metric.Index, DiagnosticMetricKind.Counter)].Value, value);

    public void Set(GaugeMetric metric, long value) =>
        Interlocked.Exchange(ref _slots[Validate(metric.Index, DiagnosticMetricKind.Gauge)].Value, value);

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
        ref var slot = ref _slots[index];
        Interlocked.Exchange(ref slot.Value, elapsedStopwatchTicks);
        Interlocked.Increment(ref slot.Count);
        Interlocked.Add(ref slot.Total, elapsedStopwatchTicks);
        Interlocked.Increment(ref _histograms[_definitions[index].HistogramSlot * DurationHistogram.BucketCount + DurationHistogram.BucketOf(elapsedStopwatchTicks)]);

        long observed;
        while (elapsedStopwatchTicks > (observed = Interlocked.Read(ref slot.Maximum)) &&
               Interlocked.CompareExchange(ref slot.Maximum, elapsedStopwatchTicks, observed) != observed)
        {
        }
    }

    internal int MetricCount => _definitions.Length;

    /// <summary>Length of the flat histogram table: one <see cref="DurationHistogram.BucketCount"/>-bucket row per timing.</summary>
    internal int HistogramLength => _histograms.Length;

    internal void CopyHistograms(long[] destination)
    {
        for (var i = 0; i < _histograms.Length; i++) destination[i] = Interlocked.Read(ref _histograms[i]);
    }

    internal MetricDefinition DefinitionAt(int index) => _definitions[index];

    /// <summary>Current value of a counter or gauge, or the latest duration of a timing (stopwatch ticks).</summary>
    internal long ReadValue(int index) => Interlocked.Read(ref _slots[index].Value);

    internal (long Count, long Total, long Maximum, long Last) ReadTiming(int index) => (
        Interlocked.Read(ref _slots[index].Count),
        Interlocked.Read(ref _slots[index].Total),
        Interlocked.Read(ref _slots[index].Maximum),
        Interlocked.Read(ref _slots[index].Value));

    internal void CopyRawValues(long[] values, long[] counts, long[] totals, long[] maximums)
    {
        for (var index = 0; index < _definitions.Length; index++)
        {
            values[index] = Interlocked.Read(ref _slots[index].Value);
            counts[index] = Interlocked.Read(ref _slots[index].Count);
            totals[index] = Interlocked.Read(ref _slots[index].Total);
            maximums[index] = Interlocked.Read(ref _slots[index].Maximum);
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
    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, indented ? JsonOptions.Indented : JsonOptions.Compact);

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
