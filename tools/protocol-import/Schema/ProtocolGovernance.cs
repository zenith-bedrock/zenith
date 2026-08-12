using Zenith.ProtocolImport.Scaffolding;

namespace Zenith.ProtocolImport.Schema;

internal sealed record PacketSyncIssue(string Packet, string Field, string Reason);

internal sealed record ProtocolGovernance(
    string Protocol,
    double Coverage,
    int GeneratedPackets,
    int ManualPackets,
    IReadOnlyList<UnsupportedConstruct> Red,
    IReadOnlyList<UnsupportedConstruct> Yellow,
    IReadOnlyList<PacketSyncIssue> GeneratedOutOfDate,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Classifies source schema capability and checks only the scalar contract a generated packet is
/// expected to own. Tier B/manual packets and unsupported shapes remain reported, never inferred
/// as generator failures.
/// </summary>
internal static class ProtocolGovernanceAnalyzer
{
    public static ProtocolGovernance Analyze(
        string protocol, IReadOnlyList<PacketSchema> schemas,
        IReadOnlyDictionary<string, IReadOnlyList<string>> generated,
        int manualPackets, IReadOnlyList<string> manifestErrors)
    {
        var red = new List<UnsupportedConstruct>();
        var yellow = new List<UnsupportedConstruct>();
        var supported = 0;
        var actionable = 0;
        foreach (var packet in schemas)
        foreach (var field in packet.Fields)
        {
            if (field.IsConstantLiteral || field.Construct == SchemaConstruct.Constant) continue;
            actionable++;
            if (field.Construct is SchemaConstruct.Union or SchemaConstruct.Unknown || field.IsComplexType)
            {
                red.Add(new UnsupportedConstruct(packet.Name, field.Name,
                    field.UnsupportedReason ?? "union or unknown schema construct"));
                continue;
            }
            if (field.Construct == SchemaConstruct.Array || field.RepeatPrefix is not null || field.Reference is not null)
            {
                yellow.Add(new UnsupportedConstruct(packet.Name, field.Name,
                    field.UnsupportedReason ?? "array or reference requires capability review"));
                continue;
            }
            if (KnownTypeMap.Resolve(field.Type).Kind == WireEmissionKind.Unknown)
            {
                red.Add(new UnsupportedConstruct(packet.Name, field.Name, $"unknown wire type '{field.Type}'"));
                continue;
            }
            supported++;
        }

        var schemaByName = schemas.ToDictionary(packet => packet.Name, StringComparer.Ordinal);
        var sync = new List<PacketSyncIssue>();
        foreach (var (name, localFields) in generated)
        {
            if (!schemaByName.TryGetValue(name, out var schema)) continue;
            var expected = schema.Fields.Where(IsSafelyGeneratedScalar)
                .Select(field => PropertyNamer.ToPascalCase(field.Name)).ToHashSet(StringComparer.Ordinal);
            var local = localFields.ToHashSet(StringComparer.Ordinal);
            foreach (var missing in expected.Except(local, StringComparer.Ordinal).OrderBy(x => x))
                sync.Add(new PacketSyncIssue(name, missing, "schema scalar field is missing from generated packet"));
            foreach (var extra in local.Except(expected, StringComparer.Ordinal).OrderBy(x => x))
                sync.Add(new PacketSyncIssue(name, extra, "generated packet field is absent or no longer scalar in schema"));
        }

        var errors = manifestErrors.ToList();
        if (schemas.Count == 0) errors.Add("No cached packet schemas; pull a verified snapshot first.");
        return new ProtocolGovernance(protocol,
            actionable == 0 ? 1 : (double)supported / actionable,
            generated.Count, manualPackets, red, yellow, sync, errors);
    }

    private static bool IsSafelyGeneratedScalar(FieldSchema field) =>
        !field.IsConstantLiteral && field.Construct == SchemaConstruct.Scalar &&
        !field.IsComplexType && field.RepeatPrefix is null && field.Reference is null &&
        KnownTypeMap.Resolve(field.Type).Kind != WireEmissionKind.Unknown;
}
