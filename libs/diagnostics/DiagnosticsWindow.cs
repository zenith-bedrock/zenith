using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Zenith.Diagnostics;

/// <summary>One timing over a window: how many samples, and the percentiles of those samples only (stopwatch ticks, bucket upper bounds).</summary>
public readonly record struct DiagnosticsTimingWindow(
    string Name, DiagnosticCategory Category, string? Parent, long Count,
    long P50StopwatchTicks, long P90StopwatchTicks, long P99StopwatchTicks, long P999StopwatchTicks, long MaximumStopwatchTicks);

/// <summary>What happened between two <see cref="DiagnosticsWindow.Capture"/> calls.</summary>
public sealed record DiagnosticsWindowSnapshot(
    DateTimeOffset From, DateTimeOffset To, long StopwatchFrequency, DiagnosticsTimingWindow[] Timings)
{
    public double ToMilliseconds(long stopwatchTicks) => stopwatchTicks * 1000d / StopwatchFrequency;

    public bool TryGet(string name, out DiagnosticsTimingWindow timing)
    {
        foreach (var candidate in Timings)
            if (candidate.Name == name)
            {
                timing = candidate;
                return true;
            }

        timing = default;
        return false;
    }

    public string ToConsole(int maximum = 10)
    {
        var output = new StringBuilder();
        output.Append("timings over ").Append((To - From).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)).AppendLine("s (ms)");
        foreach (var timing in Timings.Where(t => t.Count > 0).OrderByDescending(t => t.P99StopwatchTicks).Take(maximum))
            output.Append(timing.Name).Append(" n=").Append(timing.Count)
                .Append(" p50=").Append(Ms(timing.P50StopwatchTicks)).Append(" p99=").Append(Ms(timing.P99StopwatchTicks))
                .Append(" p99.9=").Append(Ms(timing.P999StopwatchTicks)).Append(" max<=").Append(Ms(timing.MaximumStopwatchTicks)).AppendLine();
        return output.ToString().TrimEnd();

        string Ms(long ticks) => ToMilliseconds(ticks).ToString("F3", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// A reader's own view of "since I last looked". The runtime's histograms only ever grow, so a window is the difference between
/// two copies of them: each reader keeps its own previous copy, nothing is reset on the hot path, and several readers (the
/// <c>/diagnostics</c> command, the metrics exporter) can each have a window of their own without disturbing each other. Unlike the
/// lifetime maximum, a window's percentiles describe recent behaviour, so a server that has been up for days still shows today's tail.
/// A <see cref="DiagnosticsWindow"/> is for one reader at a time; its capture is a cold-path call that allocates its result.
/// </summary>
public sealed class DiagnosticsWindow
{
    private readonly DiagnosticsRuntime _runtime;
    private readonly TimeProvider _time;
    private readonly long[] _previous;
    private readonly long[] _current;
    private DateTimeOffset _from;

    public DiagnosticsWindow(DiagnosticsRuntime runtime, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _time = time ?? TimeProvider.System;
        _previous = new long[runtime.HistogramLength];
        _current = new long[runtime.HistogramLength];
        runtime.CopyHistograms(_previous);
        _from = _time.GetUtcNow();
    }

    /// <summary>The window that started at construction or at the previous capture and ends now; starts the next one.</summary>
    public DiagnosticsWindowSnapshot Capture()
    {
        var now = _time.GetUtcNow();
        _runtime.CopyHistograms(_current);
        for (var i = 0; i < _current.Length; i++)
        {
            var delta = _current[i] - _previous[i];
            _previous[i] = _current[i];
            _current[i] = delta < 0 ? 0 : delta;
        }

        var timings = new List<DiagnosticsTimingWindow>();
        for (var index = 0; index < _runtime.MetricCount; index++)
        {
            var definition = _runtime.DefinitionAt(index);
            if (definition.Kind != DiagnosticMetricKind.Timing) continue;
            var slice = new ReadOnlySpan<long>(_current, definition.HistogramSlot * DurationHistogram.BucketCount, DurationHistogram.BucketCount);
            long count = 0;
            foreach (var bucket in slice) count += bucket;
            timings.Add(new DiagnosticsTimingWindow(
                definition.Name, definition.Category, definition.ParentIndex < 0 ? null : _runtime.DefinitionAt(definition.ParentIndex).Name, count,
                DurationHistogram.Percentile(slice, 0.50), DurationHistogram.Percentile(slice, 0.90),
                DurationHistogram.Percentile(slice, 0.99), DurationHistogram.Percentile(slice, 0.999),
                DurationHistogram.MaximumBound(slice)));
        }

        var window = new DiagnosticsWindowSnapshot(_from, now, Stopwatch.Frequency, [.. timings]);
        _from = now;
        return window;
    }
}
