using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Scaffolding;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class ReconcileSettings : CommandSettings
{
    [CommandOption("--source <NAME>")] public string Source { get; set; } = SchemaSourceFactory.Endstone;
    [CommandOption("--from <REF_OR_SHA>")] public string From { get; set; } = "";
    [CommandOption("--to <REF_OR_SHA>")] public string To { get; set; } = "";
    [CommandOption("--cache <DIR>")] public string Cache { get; set; } = ".cache";
    [CommandOption("--packets-dir <DIR>")] public string? PacketsDir { get; set; }
    [CommandOption("--tests-dir <DIR>")] public string? TestsDir { get; set; }
    [CommandOption("--report <FILE>")] public string? Report { get; set; }
    [CommandOption("--apply-green")]
    [Description("Write only additive, scalar patches owned by protocol-import. Never rewrites or removes hand-authored packet source.")]
    public bool ApplyGreen { get; set; }
}

/// <summary>
/// Reconciles local packet work toward an explicit target snapshot. Direction is intentionally
/// neutral: from a newer snapshot to an older one is a downgrade; the same safety rules apply.
/// </summary>
internal sealed class ReconcileCommand : Command<ReconcileSettings>
{
    public override int Execute(CommandContext context, ReconcileSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var fromRoot = SchemaCache.FindSnapshot(settings.Cache, source.Name, settings.From);
        var toRoot = SchemaCache.FindSnapshot(settings.Cache, source.Name, settings.To);
        if (fromRoot is null || toRoot is null)
        {
            AnsiConsole.MarkupLine("[red]Reconcile requires two pulled snapshots. Run pull for --from and --to first.[/]");
            return 1;
        }

        var errors = SchemaCache.ValidateSnapshot(fromRoot).Concat(SchemaCache.ValidateSnapshot(toRoot)).ToList();
        if (errors.Count > 0)
        {
            foreach (var error in errors) AnsiConsole.MarkupLine($"[red]Snapshot invalid:[/] {error.EscapeMarkup()}");
            return 1;
        }

        var from = source.ListCachedPackets(fromRoot).Select(name => source.ReadPacket(fromRoot, name)!).ToList();
        var to = source.ListCachedPackets(toRoot).Select(name => source.ReadPacket(toRoot, name)!).ToList();
        var manual = LocalManualPackets(settings.PacketsDir);
        var summary = ProtocolUpgradeAnalyzer.Analyze(SchemaDiffAnalyzer.Analyze(from, to, manual.Contains));
        var fromManifest = SchemaCache.ReadSnapshotManifest(fromRoot)!;
        var toManifest = SchemaCache.ReadSnapshotManifest(toRoot)!;
        var report = settings.Report ?? DefaultReportPath(settings.From, settings.To);
        UpgradeReportWriter.Write(report, fromManifest, toManifest, summary, SchemaCoverageAnalyzer.Analyze(to), "reconcile");

        if (settings.ApplyGreen)
            GreenUpgradePatchApplier.Apply(summary.Green, to, settings.PacketsDir, settings.TestsDir);

        AnsiConsole.Write(new Rule("Protocol reconciliation summary"));
        WriteList("Target-added packets", summary.AddedPackets);
        WriteList("Target-removed packets", summary.RemovedPackets);
        WriteChanges("GREEN — guarded generated additions", summary.Green, "green");
        WriteChanges("YELLOW — target-shape review", summary.Yellow, "yellow");
        WriteChanges("RED — manual action required", summary.Red, "red");
        WriteList("Recommended actions", summary.RecommendedActions);
        AnsiConsole.MarkupLine("[grey]Existing hand-authored fields are never removed automatically; target removals stay reviewable YELLOW/RED actions.[/]");
        AnsiConsole.MarkupLine($"[green]Persistent report:[/] {report.EscapeMarkup()}");
        return summary.Red.Count == 0 ? 0 : 1;
    }

    private static string DefaultReportPath(string from, string to)
    {
        var root = RepoLocator.FindRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        static string Clean(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(root, "docs", "protocol-reconciliations", $"{Clean(from)}-to-{Clean(to)}.md");
    }

    private static HashSet<string> LocalManualPackets(string? packetsDir)
    {
        if (packetsDir is null && RepoLocator.FindRoot(Directory.GetCurrentDirectory()) is { } root)
            packetsDir = Path.Combine(root, "src", "zenith", "Packets");
        if (packetsDir is null || !Directory.Exists(packetsDir)) return [];
        return Directory.GetFiles(packetsDir, "*.cs")
            .Where(path => !File.ReadAllText(path).Contains("[GamePacket("))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void WriteList(string title, IEnumerable<string> values)
    {
        var items = values.ToList();
        AnsiConsole.MarkupLine($"[bold]{title}:[/]");
        if (items.Count == 0) AnsiConsole.MarkupLine("  [grey]none[/]");
        foreach (var item in items) AnsiConsole.MarkupLine($"  - {item.EscapeMarkup()}");
    }

    private static void WriteChanges(string title, IEnumerable<SchemaChange> changes, string color) =>
        WriteList(title, changes.Select(change => $"[{color}]{change.Packet.EscapeMarkup()}[/]: {change.Description.EscapeMarkup()}"));
}
