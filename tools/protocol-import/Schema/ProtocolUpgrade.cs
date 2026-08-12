namespace Zenith.ProtocolImport.Schema;

internal sealed record UpgradeSummary(
    IReadOnlyList<string> AddedPackets,
    IReadOnlyList<string> RemovedPackets,
    IReadOnlyList<string> ChangedPackets,
    IReadOnlyList<SchemaChange> Green,
    IReadOnlyList<SchemaChange> Yellow,
    IReadOnlyList<SchemaChange> Red,
    IReadOnlyList<string> RecommendedActions);

internal static class ProtocolUpgradeAnalyzer
{
    public static UpgradeSummary Analyze(ProtocolDiff diff)
    {
        var green = diff.Changes.Where(c => c.Severity == DiffSeverity.Green).ToList();
        var yellow = diff.Changes.Where(c => c.Severity == DiffSeverity.Yellow).ToList();
        var red = diff.Changes.Where(c => c.Severity == DiffSeverity.Red).ToList();
        var actions = new List<string>();
        if (green.Count > 0) actions.Add("Regenerate or review affected Tier A packets.");
        foreach (var packet in red.Select(c => c.Packet).Distinct(StringComparer.Ordinal).OrderBy(x => x))
            actions.Add($"Review manual packet {packet}.");
        if (yellow.Count > 0) actions.Add("Review YELLOW arrays, references, and optionality changes before migration.");
        if (actions.Count == 0) actions.Add("No packet migration action is required.");
        return new UpgradeSummary(diff.AddedPackets, diff.RemovedPackets,
            diff.Changes.Select(c => c.Packet).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList(),
            green, yellow, red, actions);
    }
}
