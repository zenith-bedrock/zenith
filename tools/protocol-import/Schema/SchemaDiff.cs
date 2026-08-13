namespace Zenith.ProtocolImport.Schema;

internal enum DiffSeverity { Green, Yellow, Red }
internal sealed record SchemaChange(string Packet, string Description, DiffSeverity Severity);
internal sealed record ProtocolDiff(IReadOnlyList<string> AddedPackets, IReadOnlyList<string> RemovedPackets, IReadOnlyList<SchemaChange> Changes);

internal static class SchemaDiffAnalyzer
{
    public static ProtocolDiff Analyze(IEnumerable<PacketSchema> from, IEnumerable<PacketSchema> to, Func<string, bool>? isManualPacket = null)
    {
        var before = from.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var after = to.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var added = after.Keys.Except(before.Keys, StringComparer.Ordinal).OrderBy(x => x).ToList();
        var removed = before.Keys.Except(after.Keys, StringComparer.Ordinal).OrderBy(x => x).ToList();
        var changes = new List<SchemaChange>();
        foreach (var name in before.Keys.Intersect(after.Keys, StringComparer.Ordinal).OrderBy(x => x))
        {
            // Literal tags frequently have no schema name, and several may occur in one packet.
            // They are real wire data but not settable packet properties, so never key them by an
            // empty name or silently collapse them. Report their presence as review work instead.
            var oldAnonymous = before[name].Fields.Where(field => string.IsNullOrWhiteSpace(field.Name)).ToList();
            var newAnonymous = after[name].Fields.Where(field => string.IsNullOrWhiteSpace(field.Name)).ToList();
            if (oldAnonymous.Count != newAnonymous.Count ||
                !oldAnonymous.SequenceEqual(newAnonymous))
            {
                changes.Add(Change(name, "Changed anonymous constant/tag layout", DiffSeverity.Yellow, isManualPacket));
            }

            var oldFields = NamedFields(before[name]);
            var newFields = NamedFields(after[name]);
            foreach (var field in newFields.Values.Where(f => !oldFields.ContainsKey(f.Name)))
                changes.Add(Change(name, $"Added field: {field.Name}", SeverityFor(field), isManualPacket));
            foreach (var field in oldFields.Values.Where(f => !newFields.ContainsKey(f.Name)))
                changes.Add(Change(name, $"Removed field: {field.Name}", DiffSeverity.Yellow, isManualPacket));
            foreach (var field in newFields.Values.Where(f => oldFields.ContainsKey(f.Name)))
            {
                var old = oldFields[field.Name];
                if (old.Optional != field.Optional)
                    changes.Add(Change(name, $"Changed optionality: {field.Name}", DiffSeverity.Yellow, isManualPacket));
                if (old.Type != field.Type || old.Construct != field.Construct || old.Reference != field.Reference)
                    changes.Add(Change(name, $"Changed wire shape: {field.Name}", Max(SeverityFor(old), SeverityFor(field)), isManualPacket));
            }
        }
        return new ProtocolDiff(added, removed, changes);
    }

    private static Dictionary<string, FieldSchema> NamedFields(PacketSchema packet) => packet.Fields
        .Where(field => !string.IsNullOrWhiteSpace(field.Name))
        .GroupBy(field => field.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

    private static DiffSeverity SeverityFor(FieldSchema field) => field.Construct is SchemaConstruct.Union or SchemaConstruct.Unknown
        ? DiffSeverity.Red : field.Construct == SchemaConstruct.Array || field.Reference is not null ? DiffSeverity.Yellow : DiffSeverity.Green;
    private static DiffSeverity Max(DiffSeverity left, DiffSeverity right) => left > right ? left : right;
    private static SchemaChange Change(string packet, string description, DiffSeverity severity, Func<string, bool>? isManualPacket) =>
        isManualPacket?.Invoke(packet) == true
            ? new SchemaChange(packet, description + " (local Tier B/manual packet)", DiffSeverity.Red)
            : new SchemaChange(packet, description, severity);
}
