using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class CoverageSettings : CommandSettings
{
    [CommandOption("--source <NAME>")] [Description("Schema provider: endstone (default) or mojang.")]
    public string Source { get; set; } = SchemaSourceFactory.Endstone;
    [CommandOption("--cache <DIR>")] public string Cache { get; set; } = ".cache";
    [CommandOption("--packets-dir <DIR>")] public string? PacketsDir { get; set; }
}

/// <summary>Explicit generator/manual/unsupported view used before a protocol upgrade.</summary>
internal sealed class CoverageCommand : Command<CoverageSettings>
{
    public override int Execute(CommandContext context, CoverageSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var schemas = source.ListCachedPackets(settings.Cache)
            .Select(name => source.ReadPacket(settings.Cache, name)).Where(packet => packet is not null)
            .Cast<PacketSchema>().ToList();
        var packetsDir = settings.PacketsDir ?? (RepoLocator.FindRoot(Directory.GetCurrentDirectory()) is { } root
            ? Path.Combine(root, "src", "zenith", "Packets") : null);
        var files = packetsDir is not null && Directory.Exists(packetsDir)
            ? Directory.GetFiles(packetsDir, "*.cs") : [];
        var generated = files.Where(path => File.ReadAllText(path).Contains("[GamePacket(")).Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToList();
        var manual = files.Where(path => !File.ReadAllText(path).Contains("[GamePacket(")).Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToList();
        var coverage = SchemaCoverageAnalyzer.Analyze(schemas);
        AnsiConsole.MarkupLine($"[bold]Schema coverage:[/] {schemas.Count} cached packet schemas");
        WriteList("Generated packets", generated!); WriteList("Manual packets (Tier B / outbound-only included)", manual!);
        AnsiConsole.MarkupLine($"[bold]Unsupported schema fields:[/] {coverage.Unsupported.Count}");
        foreach (var item in coverage.Unsupported.OrderBy(x => x.Packet).ThenBy(x => x.Field))
            AnsiConsole.MarkupLine($"  [red]{item.Packet.EscapeMarkup()}.{item.Field.EscapeMarkup()}[/]: {item.Reason.EscapeMarkup()}");
        return 0;
    }
    private static void WriteList(string heading, IEnumerable<string?> items)
    {
        AnsiConsole.MarkupLine($"[bold]{heading}:[/]");
        var values = items.Where(x => x is not null).Cast<string>().ToList();
        if (values.Count == 0) AnsiConsole.MarkupLine("  [grey]none[/]");
        foreach (var value in values) AnsiConsole.MarkupLine($"  - {value.EscapeMarkup()}");
    }
}
