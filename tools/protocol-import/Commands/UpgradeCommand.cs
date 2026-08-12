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
            ApplyGreenAdditions(summary.Green, to, settings.PacketsDir, settings.TestsDir);
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
    private static void ApplyGreenAdditions(
        IEnumerable<SchemaChange> changes, IReadOnlyList<PacketSchema> target,
        string? packetsDir, string? testsDir)
    {
        var root = RepoLocator.FindRoot(Directory.GetCurrentDirectory());
        packetsDir ??= root is null ? null : Path.Combine(root, "src", "zenith", "Packets");
        testsDir ??= root is null ? null : Path.Combine(root, "src", "zenith.Tests");
        if (packetsDir is null || testsDir is null || !Directory.Exists(packetsDir) || !Directory.Exists(testsDir))
        {
            AnsiConsole.MarkupLine("[red]--apply-green requires existing --packets-dir and --tests-dir.[/]");
            return;
        }
        var schemas = target.ToDictionary(packet => packet.Name, StringComparer.Ordinal);
        foreach (var change in changes.Where(change => change.Description.StartsWith("Added field: ", StringComparison.Ordinal)))
        {
            if (!schemas.TryGetValue(change.Packet, out var packet)) continue;
            var fieldName = change.Description["Added field: ".Length..];
            var field = packet.Fields.FirstOrDefault(candidate => candidate.Name == fieldName);
            var packetPath = Path.Combine(packetsDir, packet.Name + ".cs");
            if (field is null || !File.Exists(packetPath) || !File.ReadAllText(packetPath).Contains("[GamePacket("))
            {
                AnsiConsole.MarkupLine($"[yellow]Suggest only:[/] {change.Packet.EscapeMarkup()} — no generated local packet file to extend.");
                continue;
            }
            var property = PropertyNamer.ToPascalCase(field.Name);
            if (ExistingPacketReader.ReadWirePropertyNames(packetPath).Contains(property)) continue;
            if (!GreenAttributePatchScaffolder.TryScaffold(packet, field, out var patch, out var reason))
            {
                AnsiConsole.MarkupLine($"[yellow]Suggest only:[/] {change.Packet.EscapeMarkup()}.{field.Name.EscapeMarkup()} — {reason.EscapeMarkup()}.");
                continue;
            }
            var output = Path.Combine(packetsDir, $"{packet.Name}.ProtocolUpgrade.g.cs");
            var testOutput = Path.Combine(testsDir, $"{packet.Name}ProtocolUpgradeTests.cs");
            if (File.Exists(output))
            {
                AnsiConsole.MarkupLine($"[yellow]Suggest only:[/] {output.EscapeMarkup()} already exists; never overwriting generated upgrade work.");
                continue;
            }
            if (File.Exists(testOutput))
            {
                AnsiConsole.MarkupLine($"[yellow]Suggest only:[/] {testOutput.EscapeMarkup()} already exists; never overwriting test work.");
                continue;
            }
            File.WriteAllText(output, patch);
            File.WriteAllText(testOutput, BasicTest(packet, property));
            AnsiConsole.MarkupLine($"[green]Generated GREEN attribute patch + basic round-trip test:[/] {packet.Name.EscapeMarkup()}.{property.EscapeMarkup()}");
        }
    }
    private static string BasicTest(PacketSchema packet, string property) => $$"""
using Xunit;
using Zenith.Packets;
using Zenith.Raknet.Stream;

namespace Zenith.Tests;

public sealed class {{packet.Name}}ProtocolUpgradeTests
{
    [Fact]
    public void {{packet.Name}}_{{property}}_default_value_decodes_after_upgrade()
    {
        var encoded = new {{packet.Name}}().Encode().ToArray();
        var stream = new BinaryStream(encoded);
        var decoded = new {{packet.Name}}();
        decoded.Decode(ref stream);
        stream.Dispose();
        Assert.Equal(default, decoded.{{property}});
    }
}
""";

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
