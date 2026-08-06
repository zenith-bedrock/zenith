using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class ListSettings : CommandSettings
{
    [CommandOption("--source <NAME>")]
    [Description("Schema provider: endstone (default) or mojang.")]
    public string Source { get; set; } = SchemaSourceFactory.Endstone;

    [CommandOption("--cache <DIR>")]
    public string Cache { get; set; } = ".cache";

    [CommandOption("--packets-dir <DIR>")]
    [Description("Path to src/zenith/Packets, to cross-reference already-migrated classes. Defaults to auto-detecting the repo root (looks for zenith.sln) regardless of where the tool is invoked from.")]
    public string? PacketsDir { get; set; }
}

/// <summary>
/// `protocol-import list` - cross-references cached schema packet names against [GamePacket]
/// classes already in src/zenith/Packets. Read-only reporting.
/// </summary>
internal sealed class ListCommand : Command<ListSettings>
{
    public override int Execute(CommandContext context, ListSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var cached = source.ListCachedPackets(settings.Cache).ToHashSet(StringComparer.Ordinal);

        var packetsDir = settings.PacketsDir;
        if (packetsDir is null)
        {
            var repoRoot = RepoLocator.FindRoot(Directory.GetCurrentDirectory());
            if (repoRoot is not null)
            {
                packetsDir = Path.Combine(repoRoot, "src", "zenith", "Packets");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Couldn't find zenith.sln by walking up from the current directory - pass --packets-dir explicitly. Migration status will show as unknown.[/]");
            }
        }

        var migrated = new HashSet<string>(StringComparer.Ordinal);
        if (packetsDir is not null && Directory.Exists(packetsDir))
        {
            foreach (var file in Directory.GetFiles(packetsDir, "*.cs"))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("[GamePacket("))
                    migrated.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        var table = new Table()
            .Title($"source: {source.Name}")
            .AddColumn("Packet").AddColumn("Cached").AddColumn("Migrated ([[GamePacket]])");
        foreach (var name in cached.Union(migrated).OrderBy(n => n, StringComparer.Ordinal))
        {
            table.AddRow(
                name,
                cached.Contains(name) ? "[green]yes[/]" : "[grey]no[/]",
                migrated.Contains(name) ? "[green]yes[/]" : "[grey]no[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
