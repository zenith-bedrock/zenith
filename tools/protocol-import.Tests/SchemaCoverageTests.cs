using Xunit;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Tests;

public sealed class SchemaCoverageTests
{
    [Fact]
    public void Unsupported_constructs_are_reported_without_losing_the_field_identity()
    {
        var coverage = SchemaCoverageAnalyzer.Analyze(
        [
            new PacketSchema(63, "PlayerListPacket",
            [
                new FieldSchema("Entries", "", null, false, null, IsComplexType: true,
                    Construct: SchemaConstruct.Union, UnsupportedReason: "union discriminator not supported"),
                new FieldSchema("Ids", "mce::uuid", null, false, "<unspecified>", Construct: SchemaConstruct.Array)
            ])
        ]);

        Assert.Equal(1, coverage.ArraysRequiringReview);
        var unsupported = Assert.Single(coverage.Unsupported);
        Assert.Equal("PlayerListPacket", unsupported.Packet);
        Assert.Equal("Entries", unsupported.Field);
        Assert.Equal("union discriminator not supported", unsupported.Reason);
    }
}
