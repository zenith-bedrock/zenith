using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Scaffolding;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class UpgradeSettings : CommandSettings
{
    [CommandOption("--source <NAME>")] public string Source { get; set; } = SchemaSourceFactory.Endstone;
    [CommandOption("--from <REF_OR_SHA>")] public string From { get; set; } = "";
    [CommandOption("--to <REF_OR_SHA>")] public string To { get; set; } = "";
    [CommandOption("--cache <DIR>")] public string Cache { get; set; } = ".cache";
    [CommandOption("--packets-dir <DIR>")] public string? PacketsDir { get; set; }
    [CommandOption("--report <FILE>")] public string? Report { get; set; }
    [CommandOption("--apply-green")] public bool ApplyGreen { get; set; }
    [CommandOption("--tests-dir <DIR>")] public string? TestsDir { get; set; }
}

/// <summary>Migration plan for two already-pulled snapshots; writes code only with --apply-green.</summary>
internal sealed class UpgradeCommand : Command<UpgradeSettings>
{
    public override int Execute(CommandContext context, UpgradeSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var fromRoot = SchemaCache.FindSnapshot(settings.Cache, source.Name, settings.From);
        var toRoot = SchemaCache.FindSnapshot(settings.Cache, source.Name, settings.To);
        if (fromRoot is null || toRoot is null)
        {
            AnsiConsole.MarkupLine("[red]Upgrade requires two pulled snapshots. Run pull for --from and --to first.[/]");
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
        var reportPath = settings.Report ?? DefaultReportPath(settings.To);
        UpgradeReportWriter.Write(reportPath, fromManifest, toManifest, summary, SchemaCoverageAnalyzer.Analyze(to));
        if (settings.ApplyGreen)
            GreenUpgradePatchApplier.Apply(summary.Green, to, settings.PacketsDir, settings.TestsDir);
        AnsiConsole.Write(new Rule("Protocol bump summary"));
        WriteList("Added packets", summary.AddedPackets); WriteList("Removed packets", summary.RemovedPackets);
        WriteList("Changed packets", summary.ChangedPackets);
        WriteChanges("GREEN — generated impact", summary.Green, "green");
        WriteChanges("YELLOW — human review", summary.Yellow, "yellow");
        WriteChanges("RED — manual required", summary.Red, "red");
        WriteList("Recommended actions", summary.RecommendedActions);
        AnsiConsole.MarkupLine($"[green]Persistent report:[/] {reportPath.EscapeMarkup()}");
        return summary.Red.Count == 0 ? 0 : 1;
    }
    private static string DefaultReportPath(string targetRef)
    {
        var root = RepoLocator.FindRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        var fileName = string.Concat(targetRef.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(root, "docs", "protocol-upgrades", fileName + ".md");
    }
    private static HashSet<string> LocalManualPackets(string? packetsDir)
    {
        if (packetsDir is null && RepoLocator.FindRoot(Directory.GetCurrentDirectory()) is { } root)
            packetsDir = Path.Combine(root, "src", "zenith", "Packets");
        if (packetsDir is null || !Directory.Exists(packetsDir)) return [];
        return Directory.GetFiles(packetsDir, "*.cs")
            .Where(path => !File.ReadAllText(path).Contains("[GamePacket("))
            .Select(Path.GetFileNameWithoutExtension).Where(name => name is not null).Select(name => name!)
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
