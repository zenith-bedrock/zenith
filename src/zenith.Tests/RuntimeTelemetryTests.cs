using Zenith.Raknet;
using Zenith.Raknet.Log;
using Zenith.Server;
using Xunit;

namespace Zenith.Tests;

public sealed class RuntimeTelemetryTests
{
    [Fact]
    public void RecordTick_tracksBudgetAndFlushDuration()
    {
        var telemetry = new RuntimeTelemetry(new NullLogger());
        var raknet = new RakNetServer(port: 0);

        telemetry.RecordTick(TimeSpan.FromMilliseconds(51), players: 2, actors: 3, raknet);
        telemetry.RecordFlush(TimeSpan.FromMilliseconds(17));

        var snapshot = telemetry.Snapshot;
        Assert.Equal(1, snapshot.Ticks);
        Assert.Equal(1, snapshot.OverBudgetTicks);
        Assert.Equal(51d, snapshot.LastTickMilliseconds, precision: 3);
        Assert.Equal(17, snapshot.LastFlushMilliseconds);
    }

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
