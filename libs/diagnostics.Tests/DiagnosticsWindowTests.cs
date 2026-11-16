using System.Diagnostics;
using Xunit;

namespace Zenith.Diagnostics.Tests;

public sealed class DiagnosticsWindowTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public long Timestamp { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Timestamp;
        public override long TimestampFrequency => Stopwatch.Frequency;
        public void Advance(TimeSpan span)
        {
            Now += span;
            Timestamp += (long)(span.TotalSeconds * Stopwatch.Frequency);
        }
    }

    [Fact]
    public void A_window_holds_only_what_was_recorded_since_the_previous_capture()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var runtime = builder.Build();
        var window = new DiagnosticsWindow(runtime);

        for (var i = 0; i < 100; i++) runtime.Record(tick, 1_000);
        var first = window.Capture();
        Assert.True(first.TryGet("tick", out var firstTick));
        Assert.Equal(100, firstTick.Count);

        runtime.Record(tick, 1_000_000);
        var second = window.Capture();
        Assert.True(second.TryGet("tick", out var secondTick));
        Assert.Equal(1, secondTick.Count);
        Assert.True(secondTick.P50StopwatchTicks >= 1_000_000);

        var third = window.Capture();
        Assert.True(third.TryGet("tick", out var thirdTick));
        Assert.Equal(0, thirdTick.Count);
        Assert.Equal(0, thirdTick.P99StopwatchTicks);
    }

    [Fact]
    public void An_old_spike_leaves_the_window_but_the_lifetime_maximum_keeps_it()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var runtime = builder.Build();
        var window = new DiagnosticsWindow(runtime);
        runtime.Record(tick, 50_000_000); // the spike
        window.Capture();

        for (var i = 0; i < 1000; i++) runtime.Record(tick, 200_000);
        window.Capture().TryGet("tick", out var recent);

        Assert.True(recent.MaximumStopwatchTicks < 1_000_000);
        Assert.Equal(50_000_000, runtime.CaptureSnapshot().Metrics[0].MaximumStopwatchTicks);
    }

    [Fact]
    public void Readers_have_independent_windows()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var runtime = builder.Build();
        var a = new DiagnosticsWindow(runtime);
        var b = new DiagnosticsWindow(runtime);
        runtime.Record(tick, 10);
        runtime.Record(tick, 10);

        a.Capture();
        runtime.Record(tick, 10);

        a.Capture().TryGet("tick", out var seenByA);
        b.Capture().TryGet("tick", out var seenByB);
        Assert.Equal(1, seenByA.Count);
        Assert.Equal(3, seenByB.Count);
    }

    [Fact]
    public void Only_timings_appear_and_carry_their_parent_and_category()
    {
        var builder = new DiagnosticsBuilder();
        builder.Counter("network.packets");
        var tick = builder.Timing("tick");
        var movement = builder.Timing("tick.movement", parent: "tick");
        var runtime = builder.Build();
        var window = new DiagnosticsWindow(runtime);
        runtime.Record(tick, 100);
        runtime.Record(movement, 40);

        var snapshot = window.Capture();

        Assert.Equal(["tick", "tick.movement"], snapshot.Timings.Select(t => t.Name));
        Assert.Equal("tick", snapshot.Timings[1].Parent);
        Assert.Equal(DiagnosticCategory.Tick, snapshot.Timings[1].Category);
        Assert.Contains("tick.movement", snapshot.ToConsole());
    }

    [Fact]
    public void Recording_a_timing_still_allocates_nothing_with_the_histogram()
    {
        var builder = new DiagnosticsBuilder();
        var timing = builder.Timing("tick");
        var runtime = builder.Build();
        runtime.Record(timing, 1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            using (runtime.Begin(timing)) { }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void Concurrent_writers_lose_no_samples()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var runtime = builder.Build();
        var window = new DiagnosticsWindow(runtime);

        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 20_000; i++) runtime.Record(tick, 5_000 + i);
        });

        window.Capture().TryGet("tick", out var captured);
        Assert.Equal(160_000, captured.Count);
        Assert.Equal(160_000, runtime.CaptureSnapshot().Metrics[0].Count);
    }

    [Fact]
    public void The_meter_publishes_window_percentiles_and_refreshes_them_when_the_window_elapses()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var runtime = builder.Build();
        var time = new FakeTime();
        var name = "Zenith.Diagnostics.Test." + Guid.NewGuid().ToString("N");
        using var meter = new DiagnosticsMeter(runtime, name: name, percentileWindow: TimeSpan.FromSeconds(10), time: time);

        for (var i = 0; i < 100; i++) runtime.Record(tick, Stopwatch.Frequency / 1000); // 1 ms
        var first = MeterReader.Collect(name);
        Assert.InRange(first["tick.p99"], 0.001, 0.00126);

        runtime.Record(tick, Stopwatch.Frequency / 10); // 100 ms, inside the same window: not visible yet
        Assert.InRange(MeterReader.Collect(name)["tick.p99"], 0.001, 0.00126);

        time.Advance(TimeSpan.FromSeconds(11));
        runtime.Record(tick, Stopwatch.Frequency / 10);
        var refreshed = MeterReader.Collect(name);
        Assert.InRange(refreshed["tick.p99"], 0.1, 0.126);
        Assert.Contains("tick.p50", refreshed.Keys);
        Assert.Contains("tick.p999", refreshed.Keys);
    }
}
