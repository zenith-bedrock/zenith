using Xunit;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public sealed class ProtocolUpgradeTests
{
    [Fact]
    public void Upgrade_summary_is_stable_and_keeps_red_manual_work()
    {
        var diff = new ProtocolDiff(["NewPacket"], [],
        [
            new SchemaChange("TierAPacket", "Added field: Value", DiffSeverity.Green),
            new SchemaChange("TierBPacket", "Added field: Value (local Tier B/manual packet)", DiffSeverity.Red)
        ]);

        var summary = ProtocolUpgradeAnalyzer.Analyze(diff);

        Assert.Equal(["NewPacket"], summary.AddedPackets);
        Assert.Equal(["TierAPacket", "TierBPacket"], summary.ChangedPackets);
        Assert.Single(summary.Green);
        Assert.Single(summary.Red);
        Assert.Contains("Review manual packet TierBPacket.", summary.RecommendedActions);
    }
}
