using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Zenith.Diagnostics;

/// <summary>
/// Publishes a <see cref="DiagnosticsRuntime"/> through <c>System.Diagnostics.Metrics</c>, so <c>dotnet-counters</c>, OpenTelemetry
/// and any other <see cref="MeterListener"/> can read the same numbers the console, JSON and incident output show. The custom
/// runtime stays the source of truth and the only thing the hot path touches: every instrument here is observable, so reading the
/// atomics happens when a collector asks, never when a tick records a value. Nothing is allocated or locked while recording.
/// <para>
/// Mapping: counters become observable counters; gauges become observable gauges; a timing becomes four instruments,
/// <c>name.count</c>, <c>name.total</c> (seconds, monotonic), <c>name.max</c> (lifetime) and <c>name.last</c> (seconds), plus
/// <c>name.p50</c>, <c>name.p99</c> and <c>name.p999</c> (seconds) over a recent window (bucket upper bounds, at most 25% high). The metric category is
/// a tag. Names are kept exactly as defined in the layout.
/// </para>
/// </summary>
public sealed class DiagnosticsMeter : IDisposable
{
    public const string DefaultName = "Zenith.Diagnostics";

    private readonly Meter _meter;
    private readonly DiagnosticsWindow _window;
    private readonly TimeProvider _time;
    private readonly TimeSpan _percentileWindow;
    private readonly Lock _windowLock = new();
    private DiagnosticsWindowSnapshot? _latest;
    private long _latestAt;

    /// <param name="runtime">The fixed-layout runtime to publish.</param>
    /// <param name="units">Optional unit per metric name (UCUM, e.g. <c>"By"</c>); unlisted metrics have no unit.</param>
    /// <param name="name">Meter name; collectors subscribe by it.</param>
    /// <param name="percentileWindow">How much recent history the <c>.p50</c>/<c>.p99</c>/<c>.p999</c> gauges describe (default 15 s).</param>
    /// <param name="time">Clock for the window; tests pass a fake one.</param>
    public DiagnosticsMeter(
        DiagnosticsRuntime runtime, IReadOnlyDictionary<string, string>? units = null, string name = DefaultName,
        TimeSpan? percentileWindow = null, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _percentileWindow = percentileWindow ?? TimeSpan.FromSeconds(15);
        _window = new DiagnosticsWindow(runtime, _time);
        _meter = new Meter(name);
        var frequency = (double)Stopwatch.Frequency;

        for (var i = 0; i < runtime.MetricCount; i++)
        {
            var index = i;
            var definition = runtime.DefinitionAt(index);
            KeyValuePair<string, object?>[] tags = [new("category", definition.Category.ToString().ToLowerInvariant())];
            string? unit = null;
            units?.TryGetValue(definition.Name, out unit);

            switch (definition.Kind)
            {
                case DiagnosticMetricKind.Counter:
                    _meter.CreateObservableCounter(definition.Name, () => runtime.ReadValue(index), unit, null, tags);
                    break;
                case DiagnosticMetricKind.Gauge:
                    _meter.CreateObservableGauge(definition.Name, () => runtime.ReadValue(index), unit, null, tags);
                    break;
                case DiagnosticMetricKind.Timing:
                    _meter.CreateObservableCounter(definition.Name + ".count", () => runtime.ReadTiming(index).Count, "{timing}", null, tags);
                    _meter.CreateObservableCounter(definition.Name + ".total", () => runtime.ReadTiming(index).Total / frequency, "s", null, tags);
                    _meter.CreateObservableGauge(definition.Name + ".max", () => runtime.ReadTiming(index).Maximum / frequency, "s", null, tags);
                    _meter.CreateObservableGauge(definition.Name + ".last", () => runtime.ReadTiming(index).Last / frequency, "s", null, tags);
                    var slot = definition.HistogramSlot;
                    _meter.CreateObservableGauge(definition.Name + ".p50", () => Latest().Timings[slot].P50StopwatchTicks / frequency, "s", null, tags);
                    _meter.CreateObservableGauge(definition.Name + ".p99", () => Latest().Timings[slot].P99StopwatchTicks / frequency, "s", null, tags);
                    _meter.CreateObservableGauge(definition.Name + ".p999", () => Latest().Timings[slot].P999StopwatchTicks / frequency, "s", null, tags);
                    break;
            }
        }
    }

    /// <summary>The percentiles are over the last <c>percentileWindow</c>, refreshed lazily by whichever collector reads first; a window with no samples reads 0.</summary>
    private DiagnosticsWindowSnapshot Latest()
    {
        lock (_windowLock)
        {
            var now = _time.GetTimestamp();
            if (_latest is null || _time.GetElapsedTime(_latestAt, now) >= _percentileWindow)
            {
                _latest = _window.Capture();
                _latestAt = now;
            }

            return _latest;
        }
    }

    public void Dispose() => _meter.Dispose();
}
