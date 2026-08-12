using Xunit;
using Zenith.ProtocolImport.Commands;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public sealed class ProtocolGovernanceTests
{
    [Fact]
    public void Governance_reports_coverage_review_classes_and_stale_generated_scalar_field()
    {
        var packet = new PacketSchema(1, "SomePacket",
        [
            new FieldSchema("Time", "varint32", null, false, null),
            new FieldSchema("Entries", "Thing", null, false, null, IsComplexType: true, Construct: SchemaConstruct.Union),
            new FieldSchema("Ids", "mce::uuid", null, false, "uvarint32", Construct: SchemaConstruct.Array)
        ]);
        var report = ProtocolGovernanceAnalyzer.Analyze("1.26.50", [packet],
            new Dictionary<string, IReadOnlyList<string>> { ["SomePacket"] = [] }, 4, []);

        Assert.Equal(1d / 3d, report.Coverage, 4);
        Assert.Single(report.Red);
        Assert.Single(report.Yellow);
        var stale = Assert.Single(report.GeneratedOutOfDate);
        Assert.Equal("Time", stale.Field);
        Assert.Empty(report.ValidationErrors);
    }

    [Fact]
    public void Governance_does_not_treat_manual_or_unsupported_shapes_as_generated_drift()
    {
        var packet = new PacketSchema(1, "ManualPacket",
        [new FieldSchema("Variant", "Thing", null, false, null, IsComplexType: true, Construct: SchemaConstruct.Union)]);

        var report = ProtocolGovernanceAnalyzer.Analyze("1.26.50", [packet],
            new Dictionary<string, IReadOnlyList<string>>(), 1, []);

        Assert.Empty(report.GeneratedOutOfDate);
        Assert.Single(report.Red);
        Assert.Equal(0, report.Coverage);
    }

    [Fact]
    public void Governance_requires_a_verified_nonempty_snapshot()
    {
        var report = ProtocolGovernanceAnalyzer.Analyze("1.26.50", [],
            new Dictionary<string, IReadOnlyList<string>>(), 0, ["No valid current manifest."]);

        Assert.Contains("No valid current manifest.", report.ValidationErrors);
        Assert.Contains(report.ValidationErrors, error => error.Contains("No cached packet schemas", StringComparison.Ordinal));
    }

    [Fact]
    public void Governance_accepts_a_synchronized_generated_scalar_contract()
    {
        var packet = new PacketSchema(1, "SetTimePacket",
            [new FieldSchema("Time", "varint32", null, false, null)]);
        var report = ProtocolGovernanceAnalyzer.Analyze("1.26.50", [packet],
            new Dictionary<string, IReadOnlyList<string>> { ["SetTimePacket"] = ["Time"] }, 0, []);

        Assert.Equal(1, report.Coverage);
        Assert.Empty(report.GeneratedOutOfDate);
        Assert.Empty(report.ValidationErrors);
    }

    [Fact]
    public async Task Governance_reader_accepts_a_valid_manifest_and_synchronized_generated_packet()
    {
        var cache = Path.Combine(Path.GetTempPath(), "protocol-governance-" + Guid.NewGuid());
        var packets = Path.Combine(cache, "packets");
        Directory.CreateDirectory(packets);
        try
        {
            await SchemaCache.PublishAsync(cache, "fake", "stable", "abc123",
                (staging, _) => File.WriteAllTextAsync(Path.Combine(staging, "packet.json"), "{}"), CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(packets, "SetTimePacket.cs"), "[GamePacket(1)] sealed partial class SetTimePacket { [WireVar] public int Time { get; set; } }");
            var source = new FakeSchemaSource();
            source.Packets["SetTimePacket"] = new PacketSchema(1, "SetTimePacket",
                [new FieldSchema("Time", "varint32", null, false, null)]);

            var report = ProtocolGovernanceReader.Read("1.26.50", cache, source, packets);

            Assert.Empty(report.ValidationErrors);
            Assert.Empty(report.GeneratedOutOfDate);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }
}
