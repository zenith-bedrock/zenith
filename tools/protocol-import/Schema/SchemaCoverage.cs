namespace Zenith.ProtocolImport.Schema;

internal sealed record UnsupportedConstruct(string Packet, string Field, string Reason);
internal sealed record SchemaCoverage(int ArraysRequiringReview, IReadOnlyList<UnsupportedConstruct> Unsupported);

internal static class SchemaCoverageAnalyzer
{
    public static SchemaCoverage Analyze(IEnumerable<PacketSchema> packets)
    {
        var arrays = 0;
        var unsupported = new List<UnsupportedConstruct>();
        foreach (var packet in packets)
        foreach (var field in packet.Fields)
        {
            if (field.Construct == SchemaConstruct.Array) arrays++;
            if (field.Construct is SchemaConstruct.Union or SchemaConstruct.Unknown)
                unsupported.Add(new UnsupportedConstruct(packet.Name, field.Name,
                    field.UnsupportedReason ?? field.Construct.ToString()));
        }
        return new SchemaCoverage(arrays, unsupported);
    }
}
