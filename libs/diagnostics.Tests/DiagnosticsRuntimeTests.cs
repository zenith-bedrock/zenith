using Xunit;

namespace Zenith.Diagnostics.Tests;

public sealed class DiagnosticsRuntimeTests
{
    [Fact]
    public void CaptureSnapshot_reportsCountersGaugesTimingsAndHierarchy()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var movement = builder.Timing("tick.movement", parent: "tick");
        var players = builder.Gauge("gameplay.players");
        var packets = builder.Counter("network.packets.sent");
        var diagnostics = builder.Build();

        diagnostics.Increment(packets, 3);
        diagnostics.Set(players, 2);
        diagnostics.Record(tick, 15);
        diagnostics.Record(movement, 4);

        var snapshot = diagnostics.CaptureSnapshot(DateTimeOffset.UnixEpoch);
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.CapturedAt);
        Assert.Collection(snapshot.Metrics,
            metric => Assert.Equal(("tick", 15L, 1L), (metric.Name, metric.Value, metric.Count)),
            metric => Assert.Equal(("tick.movement", "tick", 4L), (metric.Name, metric.Parent, metric.Value)),
            metric => Assert.Equal(("gameplay.players", 2L), (metric.Name, metric.Value)),
            metric => Assert.Equal(("network.packets.sent", 3L), (metric.Name, metric.Value)));
        Assert.Contains("network.packets.sent", snapshot.ToConsole());
        Assert.Contains("\"Metrics\"", snapshot.ToJson());
    }

    [Fact]
    public void Recording_doesNotAllocateAfterConstruction()
    {
        var builder = new DiagnosticsBuilder();
        var counter = builder.Counter("counter", DiagnosticCategory.Runtime);
        var gauge = builder.Gauge("gauge", DiagnosticCategory.Runtime);
        var timing = builder.Timing("timing", DiagnosticCategory.Runtime);
        var diagnostics = builder.Build();

        diagnostics.Increment(counter);
        diagnostics.Set(gauge, 1);
        diagnostics.Record(timing, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            diagnostics.Increment(counter);
            diagnostics.Set(gauge, i);
            using (diagnostics.Begin(timing)) { }
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void Builder_rejectsUnknownOrDuplicateMetricNames()
    {
        var builder = new DiagnosticsBuilder();
        builder.Counter("network.bytes");

        Assert.Throws<ArgumentException>(() => builder.Counter("network.bytes"));
        Assert.Throws<ArgumentException>(() => builder.Gauge("network.rate", "missing"));
    }
}
