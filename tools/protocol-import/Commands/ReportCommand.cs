using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class ReportSettings : CommandSettings
{
    [CommandOption("--source <NAME>")]
    [Description("Schema provider: endstone (default) or mojang.")]
    public string Source { get; set; } = SchemaSourceFactory.Endstone;

    [CommandOption("--cache <DIR>")]
    public string Cache { get; set; } = ".cache";

    [CommandOption("--packets-dir <DIR>")]
    public string? PacketsDir { get; set; }
}

/// <summary>Read-only coverage and cache-integrity view; it never claims Tier B is a generator failure.</summary>
internal sealed class ReportCommand : Command<ReportSettings>
{
    public override int Execute(CommandContext context, ReportSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var manifest = SchemaCache.ReadCurrentManifest(settings.Cache, source.Name);
        var errors = SchemaCache.Validate(settings.Cache, source.Name);
        var cached = source.ListCachedPackets(settings.Cache);
        var packetsDir = settings.PacketsDir;
        if (packetsDir is null && RepoLocator.FindRoot(Directory.GetCurrentDirectory()) is { } root)
            packetsDir = Path.Combine(root, "src", "zenith", "Packets");
        var generated = packetsDir is not null && Directory.Exists(packetsDir)
            ? Directory.GetFiles(packetsDir, "*.cs").Count(path => File.ReadAllText(path).Contains("[GamePacket(")) : 0;
        var handwritten = packetsDir is not null && Directory.Exists(packetsDir)
            ? Directory.GetFiles(packetsDir, "*.cs").Length - generated : 0;

        AnsiConsole.MarkupLine($"[bold]Source:[/] {source.Name}");
        if (manifest is null) AnsiConsole.MarkupLine("[yellow]Cache: legacy/unverified — run pull to create a manifest.[/] ");
        else AnsiConsole.MarkupLine($"[bold]Provenance:[/] {manifest.RequestedRef} → {manifest.ResolvedSha} ({manifest.Files.Count} files)");
        foreach (var error in errors) AnsiConsole.MarkupLine($"[red]Cache invalid:[/] {error.EscapeMarkup()}");

        var schemas = new List<PacketSchema>();
        foreach (var name in cached)
        {
            var packet = source.ReadPacket(settings.Cache, name);
            if (packet is not null) schemas.Add(packet);
        }
        var coverage = SchemaCoverageAnalyzer.Analyze(schemas);

        var summary = new Table().AddColumn("Coverage").AddColumn("Count");
        summary.AddRow("GREEN — generated local packets", generated.ToString());
        summary.AddRow("YELLOW — arrays requiring capability review", coverage.ArraysRequiringReview.ToString());
        summary.AddRow("RED — source constructs requiring manual handling", coverage.Unsupported.Count.ToString());
        summary.AddRow("Manual local packet files (Tier B / outbound-only included)", handwritten.ToString());
        summary.AddRow("Cached source packets", cached.Count.ToString());
        AnsiConsole.Write(summary);
        foreach (var item in coverage.Unsupported.OrderBy(item => item.Packet).ThenBy(item => item.Field))
            AnsiConsole.MarkupLine($"[red]RED[/] {item.Packet.EscapeMarkup()}.{item.Field.EscapeMarkup()}: {item.Reason.EscapeMarkup()}");
        return errors.Count == 0 ? 0 : 1;
    }
}
