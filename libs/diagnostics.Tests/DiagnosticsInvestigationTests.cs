using Xunit;

namespace Zenith.Diagnostics.Tests;

public sealed class DiagnosticsInvestigationTests
{
    [Fact]
    public void Comparison_usesTimingAverageAndIdentifiesActorPressure()
    {
        var builder = new DiagnosticsBuilder();
        var tick = builder.Timing("tick");
        var movement = builder.Timing("tick.system.movement", "tick");
        var actors = builder.Gauge("gameplay.actors");
        var diagnostics = builder.Build();

        diagnostics.Record(tick, 20);
        diagnostics.Record(movement, 10);
        diagnostics.Set(actors, 100);
        var baseline = diagnostics.CaptureSnapshot(DateTimeOffset.UnixEpoch);

        diagnostics.Record(tick, 60);
        diagnostics.Record(movement, 50);
        diagnostics.Set(actors, 200);
        var comparison = DiagnosticsComparison.Create(baseline, diagnostics.CaptureSnapshot(DateTimeOffset.UnixEpoch.AddSeconds(1)));

        var tickComparison = Assert.Single(comparison.Metrics, metric => metric.Name == "tick");
        Assert.Equal(1d, tickComparison.RelativeIncrease, precision: 6);
        Assert.Equal(DiagnosticCategory.Gameplay, comparison.Diagnose().Category);
        Assert.Contains("actors", comparison.ToConsole());
        Assert.Contains("Metrics", comparison.ToJson());
    }

    [Fact]
    public void IncidentBuffer_keepsPreallocatedRecentRawSnapshots()
    {
        var builder = new DiagnosticsBuilder();
        var packets = builder.Counter("network.packets.sent");
        var diagnostics = builder.Build();
        var incidents = new DiagnosticsIncidentBuffer(diagnostics, capacity: 2);

        diagnostics.Increment(packets, 1);
        incidents.Record(DateTimeOffset.UnixEpoch);
        diagnostics.Increment(packets, 2);
        incidents.Record(DateTimeOffset.UnixEpoch.AddSeconds(1));
        diagnostics.Increment(packets, 3);
        incidents.Record(DateTimeOffset.UnixEpoch.AddSeconds(2));

        var recent = incidents.CaptureRecent();
        Assert.Equal(2, recent.Length);
        Assert.Equal(3, recent[0].Metrics[0].Value);
        Assert.Equal(6, recent[1].Metrics[0].Value);
    }

    [Fact]
    public void IncidentRecord_doesNotAllocateAfterBufferConstruction()
    {
        var builder = new DiagnosticsBuilder();
        var counter = builder.Counter("network.counter");
        var diagnostics = builder.Build();
        var incidents = new DiagnosticsIncidentBuffer(diagnostics);
        incidents.Record(DateTimeOffset.UnixEpoch);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            diagnostics.Increment(counter);
            incidents.Record(DateTimeOffset.UnixEpoch);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
