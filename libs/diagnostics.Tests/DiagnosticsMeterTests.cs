using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Xunit;

namespace Zenith.Diagnostics.Tests;

public sealed class DiagnosticsMeterTests
{
    [Fact]
    public void Counters_gauges_and_timings_are_published_with_their_category_and_seconds()
    {
        var builder = new DiagnosticsBuilder();
        var packets = builder.Counter("network.packets.sent");
        var players = builder.Gauge("gameplay.players");
        var tick = builder.Timing("tick");
        var runtime = builder.Build();
        var name = "Zenith.Diagnostics.Test." + Guid.NewGuid().ToString("N");
        using var meter = new DiagnosticsMeter(runtime, new Dictionary<string, string> { ["gameplay.players"] = "{player}" }, name);

        runtime.Increment(packets, 7);
        runtime.Set(players, 3);
        runtime.Record(tick, Stopwatch.Frequency / 2); // 0.5 s
        runtime.Record(tick, Stopwatch.Frequency / 4); // 0.25 s, the latest

        var seen = Collect(name);

        Assert.Equal(7d, seen["network.packets.sent"].Value);
        Assert.Equal("network", seen["network.packets.sent"].Category);
        Assert.Equal(3d, seen["gameplay.players"].Value);
        Assert.Equal("{player}", seen["gameplay.players"].Unit);
        Assert.Equal(2d, seen["tick.count"].Value);
        Assert.Equal(0.75d, seen["tick.total"].Value, precision: 6);
        Assert.Equal(0.5d, seen["tick.max"].Value, precision: 6);
        Assert.Equal(0.25d, seen["tick.last"].Value, precision: 6);
        Assert.Equal("s", seen["tick.total"].Unit);
        Assert.Equal("tick", seen["tick.max"].Category);
    }

    [Fact]
    public void Publishing_does_not_add_work_to_recording_and_reflects_later_values()
    {
        var builder = new DiagnosticsBuilder();
        var gauge = builder.Gauge("runtime.managed-bytes");
        var runtime = builder.Build();
        var name = "Zenith.Diagnostics.Test." + Guid.NewGuid().ToString("N");
        using var meter = new DiagnosticsMeter(runtime, name: name);

        runtime.Set(gauge, 10);
        Assert.Equal(10d, Collect(name)["runtime.managed-bytes"].Value);

        // Recording after the meter exists still allocates nothing: instruments only read when a collector asks.
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) runtime.Set(gauge, i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        runtime.Set(gauge, 99);
        Assert.Equal(99d, Collect(name)["runtime.managed-bytes"].Value);
    }

    private static Dictionary<string, (double Value, string? Unit, string? Category)> Collect(string meterName)
    {
        var seen = new Dictionary<string, (double, string?, string?)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName) l.EnableMeasurementEvents(instrument);
            }
        };

        void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) where T : struct
        {
            // The category is an instrument-level tag (declared once at creation), not a per-measurement tag.
            string? category = null;
            foreach (var tag in instrument.Tags ?? [])
            {
                if (tag.Key == "category") category = tag.Value as string;
            }

            seen[instrument.Name] = (Convert.ToDouble(value, CultureInfo.InvariantCulture), instrument.Unit, category);
        }

        listener.SetMeasurementEventCallback<long>(Record);
        listener.SetMeasurementEventCallback<double>(Record);
        listener.Start();
        listener.RecordObservableInstruments();
        return seen;
    }
}
