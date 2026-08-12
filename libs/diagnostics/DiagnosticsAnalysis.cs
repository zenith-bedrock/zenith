using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Zenith.Diagnostics;

/// <summary>Cold-path comparison between two snapshots with no effect on metric recording.</summary>
public sealed record DiagnosticsComparison(
    DateTimeOffset BaselineCapturedAt,
    DateTimeOffset CurrentCapturedAt,
    DiagnosticsMetricComparison[] Metrics)
{
    public static DiagnosticsComparison Create(DiagnosticsSnapshot baseline, DiagnosticsSnapshot current)
    {
        var oldMetrics = baseline.Metrics.ToDictionary(metric => metric.Name, StringComparer.Ordinal);
        var comparisons = new DiagnosticsMetricComparison[current.Metrics.Length];
        for (var index = 0; index < current.Metrics.Length; index++)
        {
            var metric = current.Metrics[index];
            if (!oldMetrics.TryGetValue(metric.Name, out var previous) || previous.Kind != metric.Kind)
                comparisons[index] = DiagnosticsMetricComparison.Added(metric);
            else
                comparisons[index] = DiagnosticsMetricComparison.Create(previous, metric, baseline.StopwatchFrequency, current.StopwatchFrequency);
        }
        return new DiagnosticsComparison(baseline.CapturedAt, current.CapturedAt, comparisons);
    }

    public DiagnosticsDiagnosis Diagnose()
    {
        var tick = Metrics.FirstOrDefault(metric => metric.Name == "tick");
        var suspect = Metrics
            .Where(metric => metric.Kind == DiagnosticMetricKind.Timing && metric.Category == DiagnosticCategory.Tick && metric.Parent == "tick")
            .OrderByDescending(metric => metric.CurrentDisplayValue)
            .FirstOrDefault();
        var gc = Metrics.Where(metric => metric.Category == DiagnosticCategory.Runtime && metric.Name.StartsWith("runtime.gc.", StringComparison.Ordinal))
            .Any(metric => metric.DeltaValue > 0);
        var network = Metrics.Where(metric => metric.Category == DiagnosticCategory.Network)
            .OrderByDescending(metric => metric.RelativeIncrease).FirstOrDefault();
        var actors = Metrics.FirstOrDefault(metric => metric.Name == "gameplay.actors");

        if (gc && tick.RelativeIncrease > 0.20)
            return new(DiagnosticCategory.Runtime, "GC activity increased while tick cost rose.", tick, suspect);
        if (network.RelativeIncrease > 0.50 && tick.RelativeIncrease > 0.20)
            return new(DiagnosticCategory.Network, "Network pressure grew materially with tick cost.", tick, network);
        if (actors.RelativeIncrease > 0.25 && tick.RelativeIncrease > 0.20)
            return new(DiagnosticCategory.Gameplay, "Actor pressure grew materially with tick cost.", tick, actors);
        if (suspect.Name is not null && tick.RelativeIncrease > 0)
            return new(DiagnosticCategory.Tick, $"The largest current system timing is {suspect.Name}.", tick, suspect);
        return new(DiagnosticCategory.Tick, "No dominant pressure change was detected; inspect the current top system timings.", tick, suspect);
    }

    public string ToConsole()
    {
        var output = new StringBuilder();
        output.Append("diagnostics compare ").Append(BaselineCapturedAt.ToString("O")).Append(" -> ")
            .Append(CurrentCapturedAt.ToString("O")).AppendLine();
        foreach (var metric in Metrics.Where(metric => metric.IsComparable).OrderByDescending(metric => Math.Abs(metric.RelativeIncrease)))
            output.Append(metric.Name).Append(": ").Append(metric.CurrentDisplayValue.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" (").Append(metric.RelativeIncrease.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture)).AppendLine(")");
        output.Append("likely: ").Append(Diagnose().Summary);
        return output.ToString();
    }

    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = indented });
}

public readonly record struct DiagnosticsMetricComparison(
    string Name, DiagnosticMetricKind Kind, DiagnosticCategory Category, string? Parent,
    long BaselineValue, long CurrentValue, long DeltaValue, double RelativeIncrease, double CurrentDisplayValue, bool IsComparable)
{
    public static DiagnosticsMetricComparison Added(DiagnosticsMetricSnapshot current) =>
        new(current.Name, current.Kind, current.Category, current.Parent, 0, current.Value, current.Value, double.NaN, Display(current, 0), false);

    public static DiagnosticsMetricComparison Create(DiagnosticsMetricSnapshot baseline, DiagnosticsMetricSnapshot current, long baselineFrequency, long currentFrequency)
    {
        var oldValue = Display(baseline, baselineFrequency);
        var newValue = Display(current, currentFrequency);
        return new(current.Name, current.Kind, current.Category, current.Parent, baseline.Value, current.Value,
            current.Value - baseline.Value, oldValue == 0d ? (newValue == 0d ? 0d : double.PositiveInfinity) : (newValue - oldValue) / oldValue,
            newValue, true);
    }

    private static double Display(DiagnosticsMetricSnapshot metric, long frequency) => metric.Kind switch
    {
        DiagnosticMetricKind.Timing when metric.Count != 0 && frequency != 0 => metric.TotalStopwatchTicks * 1000d / frequency / metric.Count,
        _ => metric.Value
    };
}

public readonly record struct DiagnosticsDiagnosis(
    DiagnosticCategory Category,
    string Summary,
    DiagnosticsMetricComparison Tick,
    DiagnosticsMetricComparison Evidence);

/// <summary>
/// Preallocated raw snapshot ring. Capture is an array copy; materializing history to public
/// snapshots happens only at the reader boundary.
/// </summary>
public sealed class DiagnosticsIncidentBuffer
{
    private readonly DiagnosticsRuntime _runtime;
    private readonly long[][] _values;
    private readonly long[][] _counts;
    private readonly long[][] _totals;
    private readonly long[][] _maximums;
    private readonly DateTimeOffset[] _capturedAt;
    private int _next;
    private int _written;

    public DiagnosticsIncidentBuffer(DiagnosticsRuntime runtime, int capacity = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _runtime = runtime;
        var metricCount = runtime.MetricCount;
        _values = Allocate(capacity, metricCount);
        _counts = Allocate(capacity, metricCount);
        _totals = Allocate(capacity, metricCount);
        _maximums = Allocate(capacity, metricCount);
        _capturedAt = new DateTimeOffset[capacity];
    }

    public void Record(DateTimeOffset capturedAt)
    {
        var index = _next;
        _runtime.CopyRawValues(_values[index], _counts[index], _totals[index], _maximums[index]);
        _capturedAt[index] = capturedAt;
        _next = (_next + 1) % _values.Length;
        if (_written < _values.Length) _written++;
    }

    public DiagnosticsSnapshot[] CaptureRecent()
    {
        var snapshots = new DiagnosticsSnapshot[_written];
        var first = (_next - _written + _values.Length) % _values.Length;
        for (var index = 0; index < _written; index++)
        {
            var slot = (first + index) % _values.Length;
            snapshots[index] = _runtime.CreateSnapshot(_capturedAt[slot], _values[slot], _counts[slot], _totals[slot], _maximums[slot]);
        }
        return snapshots;
    }

    private static long[][] Allocate(int capacity, int metricCount)
    {
        var rows = new long[capacity][];
        for (var index = 0; index < rows.Length; index++) rows[index] = new long[metricCount];
        return rows;
    }
}
