using Xunit;

namespace Zenith.Diagnostics.Tests;

/// <summary>Layout rules, handle validation, snapshot independence, the incident ring and the investigation, at their edges.</summary>
public sealed class DiagnosticsEdgeTests
{
    [Fact]
    public void A_name_without_a_known_prefix_needs_an_explicit_category()
    {
        var builder = new DiagnosticsBuilder();

        Assert.Throws<ArgumentException>(() => builder.Counter("custom.thing"));
        builder.Counter("custom.thing", DiagnosticCategory.Gameplay); // the explicit overload bypasses inference
        Assert.Throws<ArgumentException>(() => builder.Counter(" "));
        Assert.Throws<ArgumentNullException>(() => builder.Counter(null!));
    }

    [Fact]
    public void A_parent_must_be_defined_before_its_child()
    {
        var builder = new DiagnosticsBuilder();
        Assert.Throws<ArgumentException>(() => builder.Timing("tick.movement", parent: "tick"));
        builder.Timing("tick");
        builder.Timing("tick.movement", parent: "tick");
    }

    [Fact]
    public void A_handle_of_the_wrong_kind_or_from_another_layout_is_rejected()
    {
        var builder = new DiagnosticsBuilder();
        var counter = builder.Counter("network.packets");
        builder.Timing("tick");
        var runtime = builder.Build();
        var other = new DiagnosticsBuilder();
        other.Counter("a.a", DiagnosticCategory.Runtime);
        other.Counter("b.b", DiagnosticCategory.Runtime);
        var foreign = other.Counter("c.c", DiagnosticCategory.Runtime);

        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Set(new GaugeMetric(counter.Index), 1)); // a counter's index used as a gauge
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Increment(foreign)); // index past this layout
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Increment(new CounterMetric(-1)));
    }

    [Fact]
    public void Gauges_replace_counters_accumulate_and_negative_durations_are_ignored()
    {
        var builder = new DiagnosticsBuilder();
        var gauge = builder.Gauge("gameplay.players");
        var counter = builder.Counter("network.packets");
        var timing = builder.Timing("tick");
        var runtime = builder.Build();

        runtime.Set(gauge, 5);
        runtime.Set(gauge, 2);
        runtime.Increment(counter, 3);
        runtime.Increment(counter);
        runtime.Record(timing, -10);
        runtime.Record(timing, 30);
        runtime.Record(timing, 10);

        var metrics = runtime.CaptureSnapshot().Metrics;
        Assert.Equal(2, metrics[0].Value);
        Assert.Equal(4, metrics[1].Value);
        Assert.Equal((2L, 40L, 30L, 10L), (metrics[2].Count, metrics[2].TotalStopwatchTicks, metrics[2].MaximumStopwatchTicks, metrics[2].Value));
    }

    [Fact]
    public void A_snapshot_is_a_copy_that_later_recording_does_not_change()
    {
        var builder = new DiagnosticsBuilder();
        var counter = builder.Counter("network.packets");
        var runtime = builder.Build();
        runtime.Increment(counter, 5);

        var snapshot = runtime.CaptureSnapshot();
        runtime.Increment(counter, 5);

        Assert.Equal(5, snapshot.Metrics[0].Value);
        Assert.Equal(10, runtime.CaptureSnapshot().Metrics[0].Value);
    }

    [Fact]
    public void The_incident_ring_keeps_the_newest_in_order_and_drops_the_oldest()
    {
        var builder = new DiagnosticsBuilder();
        var counter = builder.Counter("network.packets");
        var runtime = builder.Build();
        var ring = new DiagnosticsIncidentBuffer(runtime, capacity: 3);
        Assert.Empty(ring.CaptureRecent());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticsIncidentBuffer(runtime, capacity: 0));

        for (var i = 1; i <= 5; i++)
        {
            runtime.Increment(counter);
            ring.Record(DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        var kept = ring.CaptureRecent();
        Assert.Equal([3L, 4L, 5L], kept.Select(s => s.Metrics[0].Value));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(3), kept[0].CapturedAt);
    }

    [Fact]
    public void The_investigation_compares_against_a_baseline_only_once_one_exists()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var runtime = builder.Build();
        var investigation = new DiagnosticsInvestigation(runtime, new DiagnosticsIncidentBuffer(runtime));

        Assert.False(investigation.TryCompareBaseline(out _));
        runtime.Record(tick, 100);
        investigation.CaptureBaseline();
        runtime.Record(tick, 300);

        Assert.True(investigation.TryCompareBaseline(out var comparison));
        Assert.True(comparison.Metrics[0].RelativeIncrease > 0);
        Assert.Contains("likely:", comparison.ToConsole());
    }

    [Fact]
    public void Top_timings_are_ordered_by_their_latest_duration()
    {
        var builder = new DiagnosticsBuilder();
        var slow = builder.Timing("tick.slow");
        var fast = builder.Timing("tick.fast");
        builder.Counter("network.packets");
        var runtime = builder.Build();
        runtime.Record(fast, 10);
        runtime.Record(slow, 90);
        var investigation = new DiagnosticsInvestigation(runtime, new DiagnosticsIncidentBuffer(runtime));

        Assert.Equal(["tick.slow", "tick.fast"], investigation.CaptureTopTimings().Select(m => m.Name));
        Assert.Single(investigation.CaptureTopTimings(1));
    }

    [Fact]
    public void A_metric_added_after_the_baseline_is_marked_not_comparable()
    {
        var before = new DiagnosticsSnapshot(DateTimeOffset.UnixEpoch, 1000, []);
        var after = new DiagnosticsSnapshot(DateTimeOffset.UnixEpoch, 1000,
            [new DiagnosticsMetricSnapshot("network.packets", DiagnosticMetricKind.Counter, DiagnosticCategory.Network, null, 5, 0, 0, 0)]);

        var comparison = DiagnosticsComparison.Create(before, after);

        Assert.False(comparison.Metrics[0].IsComparable);
        Assert.True(double.IsNaN(comparison.Metrics[0].RelativeIncrease));
    }

    [Fact]
    public void Diagnose_reads_the_metric_names_of_the_profile_it_is_given()
    {
        static DiagnosticsMetricSnapshot Timing(string name, string? parent, long ticks) =>
            new(name, DiagnosticMetricKind.Timing, DiagnosticCategory.Tick, parent, ticks, 1, ticks, ticks);

        var before = new DiagnosticsSnapshot(DateTimeOffset.UnixEpoch, 1000, [Timing("frame", null, 100), Timing("frame.physics", "frame", 50)]);
        var after = new DiagnosticsSnapshot(DateTimeOffset.UnixEpoch, 1000, [Timing("frame", null, 300), Timing("frame.physics", "frame", 250)]);
        var comparison = DiagnosticsComparison.Create(before, after);

        var byDefault = comparison.Diagnose(); // no metric called "tick": nothing to attribute
        var custom = comparison.Diagnose(new DiagnosisProfile("frame", "gameplay.actors", "runtime.gc."));

        Assert.DoesNotContain("frame.physics", byDefault.Summary);
        Assert.Contains("frame.physics", custom.Summary);
    }
}
