using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Scaffolding;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class ScaffoldSettings : CommandSettings
{
    [CommandArgument(0, "<PACKET_NAME>")]
    [Description("Packet name as it appears in the source's protocol docs, e.g. AnimatePacket.")]
    public string PacketName { get; set; } = "";

    [CommandOption("--source <NAME>")]
    [Description("Schema provider: endstone (default) or mojang.")]
    public string Source { get; set; } = SchemaSourceFactory.Endstone;

    [CommandOption("--cache <DIR>")]
    public string Cache { get; set; } = ".cache";

    [CommandOption("--out <FILE>")]
    [Description("Write to this file instead of stdout. You still need to review + move it into src/zenith/Packets yourself.")]
    public string? Out { get; set; }
}

/// <summary>
/// `protocol-import scaffold` - maps a cached packet schema onto [GamePacket]/[Wire*]. Output
/// is a starting point for human review, never auto-applied. See ADR §76 v1 scope for what
/// this does and does not handle correctly.
/// </summary>
internal sealed class ScaffoldCommand : Command<ScaffoldSettings>
{
    public override int Execute(CommandContext context, ScaffoldSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);

        var packet = SchemaLookupHelper.TryReadPacket(source, settings.Cache, settings.PacketName, settings.Source);
        if (packet is null) return 1;

        var scaffolder = new AttributeScaffolder(source, settings.Cache);
        var result = scaffolder.Scaffold(packet);

        if (settings.Out is not null)
        {
            File.WriteAllText(settings.Out, result.SourceText);
            AnsiConsole.MarkupLine($"[green]Wrote {settings.Out}[/] - review before committing.");
        }
        else
        {
            AnsiConsole.WriteLine(result.SourceText);
        }

        if (result.Notes.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Notes:[/]");
            foreach (var note in result.Notes)
                AnsiConsole.MarkupLine($"  [yellow]-[/] {note.EscapeMarkup()}");
        }

        return 0;
    }
}
