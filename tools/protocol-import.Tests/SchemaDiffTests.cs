using Xunit;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public sealed class SchemaDiffTests
{
    [Fact]
    public void Same_snapshots_have_an_empty_deterministic_diff()
    {
        var packets = new[] { new PacketSchema(1, "Packet", [new FieldSchema("Value", "int32", null, false, null)]) };
        var diff = SchemaDiffAnalyzer.Analyze(packets, packets);
        Assert.Empty(diff.AddedPackets); Assert.Empty(diff.RemovedPackets); Assert.Empty(diff.Changes);
    }

    [Fact]
    public void Field_and_unsupported_shape_changes_are_reported()
    {
        var from = new[] { new PacketSchema(1, "Packet", [new FieldSchema("Value", "int32", null, false, null)]) };
        var to = new[] { new PacketSchema(1, "Packet", [
            new FieldSchema("Value", "int32", null, true, null),
            new FieldSchema("Entries", "", null, false, null, IsComplexType: true, Construct: SchemaConstruct.Union,
                UnsupportedReason: "union discriminator not supported")]) };

        var diff = SchemaDiffAnalyzer.Analyze(from, to);
        Assert.Contains(diff.Changes, c => c.Description == "Changed optionality: Value" && c.Severity == DiffSeverity.Yellow);
        Assert.Contains(diff.Changes, c => c.Description == "Added field: Entries" && c.Severity == DiffSeverity.Red);
    }

    [Fact]
    public void Local_manual_packet_stays_red_even_for_a_scalar_change()
    {
        var from = new[] { new PacketSchema(1, "TierBPacket", []) };
        var to = new[] { new PacketSchema(1, "TierBPacket", [new FieldSchema("Value", "int32", null, false, null)]) };
        var diff = SchemaDiffAnalyzer.Analyze(from, to, packet => packet == "TierBPacket");
        Assert.Contains(diff.Changes, c => c.Severity == DiffSeverity.Red && c.Description.Contains("Tier B/manual"));
    }

    [Fact]
    public void Reverse_target_reconciliation_keeps_field_removal_reviewable()
    {
        var newer = new[] { new PacketSchema(1, "Packet", [new FieldSchema("Legacy", "int32", null, false, null)]) };
        var olderTarget = new[] { new PacketSchema(1, "Packet", []) };

        var diff = SchemaDiffAnalyzer.Analyze(newer, olderTarget);
        var summary = ProtocolUpgradeAnalyzer.Analyze(diff);

        Assert.Empty(summary.Green);
        Assert.Contains(summary.Yellow, change => change.Description == "Removed field: Legacy");
        Assert.Contains("Review YELLOW arrays, references, and optionality changes before migration.", summary.RecommendedActions);
    }

    [Fact]
    public void Anonymous_constant_tags_are_reviewable_instead_of_colliding_in_a_field_map()
    {
        var before = new[] { new PacketSchema(1, "Packet", [
            new FieldSchema("", "bool", null, false, null, IsConstantLiteral: true, Construct: SchemaConstruct.Constant),
            new FieldSchema("", "bool", null, false, null, IsConstantLiteral: true, Construct: SchemaConstruct.Constant)]) };
        var target = new[] { new PacketSchema(1, "Packet", [
            new FieldSchema("", "bool", null, false, null, IsConstantLiteral: true, Construct: SchemaConstruct.Constant)]) };

        var diff = SchemaDiffAnalyzer.Analyze(before, target);

        Assert.Contains(diff.Changes, change => change.Description == "Changed anonymous constant/tag layout" && change.Severity == DiffSeverity.Yellow);
    }
}
