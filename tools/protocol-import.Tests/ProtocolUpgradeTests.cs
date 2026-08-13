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

    [Fact]
    public void Upgrade_report_persists_snapshots_decisions_and_manual_actions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"protocol-upgrade-{Guid.NewGuid():N}.md");
        try
        {
            var summary = new UpgradeSummary([], [], ["TierBPacket"], [], [],
                [new SchemaChange("TierBPacket", "Changed wire shape: Value", DiffSeverity.Red)],
                ["Review manual packet TierBPacket."]);
            UpgradeReportWriter.Write(path,
                new CacheManifest("mojang", "r/26_u3", "before", DateTimeOffset.UnixEpoch, []),
                new CacheManifest("mojang", "r/26_u4", "after", DateTimeOffset.UnixEpoch, []),
                summary,
                new SchemaCoverage(0, [new UnsupportedConstruct("TierBPacket", "Value", "union") ]));

            var report = File.ReadAllText(path);
            Assert.Contains("r/26_u3", report);
            Assert.Contains("r/26_u4", report);
            Assert.Contains("Changed packets", report);
            Assert.Contains("Decisions", report);
            Assert.Contains("Manual actions", report);
            Assert.Contains("union", report);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Reconcile_report_identifies_the_target_state_without_calling_it_a_bump()
    {
        var path = Path.Combine(Path.GetTempPath(), $"protocol-reconcile-{Guid.NewGuid():N}.md");
        try
        {
            UpgradeReportWriter.Write(path,
                new CacheManifest("endstone", "r26_u5", "newer", DateTimeOffset.UnixEpoch, []),
                new CacheManifest("endstone", "r26_u4", "target", DateTimeOffset.UnixEpoch, []),
                new UpgradeSummary([], [], [], [], [], [], ["No packet migration action is required."]),
                new SchemaCoverage(0, []), "reconcile");

            var report = File.ReadAllText(path);
            Assert.Contains("protocol reconcile", report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("r26_u5", report);
            Assert.Contains("r26_u4", report);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Report_allows_a_filename_in_the_current_directory()
    {
        var name = $"protocol-report-{Guid.NewGuid():N}.md";
        try
        {
            UpgradeReportWriter.Write(name,
                new CacheManifest("endstone", "from", "from", DateTimeOffset.UnixEpoch, []),
                new CacheManifest("endstone", "to", "to", DateTimeOffset.UnixEpoch, []),
                new UpgradeSummary([], [], [], [], [], [], []), new SchemaCoverage(0, []));
            Assert.True(File.Exists(name));
        }
        finally
        {
            if (File.Exists(name)) File.Delete(name);
        }
    }
}
