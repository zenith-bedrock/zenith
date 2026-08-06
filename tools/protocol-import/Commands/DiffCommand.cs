using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Scaffolding;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class DiffSettings : CommandSettings
{
    [CommandArgument(0, "<PACKET_NAME>")]
    public string PacketName { get; set; } = "";

    [CommandOption("--source <NAME>")]
    [Description("Schema provider: endstone (default) or mojang.")]
    public string Source { get; set; } = SchemaSourceFactory.Endstone;

    [CommandOption("--file <PATH>")]
    [Description("Path to the already-migrated [GamePacket] .cs file to compare against.")]
    public string File { get; set; } = "";

    [CommandOption("--cache <DIR>")]
    public string Cache { get; set; } = ".cache";
}

/// <summary>
/// `protocol-import diff` - compares an already-migrated packet's wire-attributed property
/// list against a freshly cached schema's field list. This is the actual point of the tool:
/// turning a protocol version bump into a reviewable diff instead of a re-read of the packet.
/// </summary>
internal sealed class DiffCommand : Command<DiffSettings>
{
    public override int Execute(CommandContext context, DiffSettings settings)
    {
        if (string.IsNullOrEmpty(settings.File) || !System.IO.File.Exists(settings.File))
        {
            AnsiConsole.MarkupLine($"[red]--file '{settings.File}' not found.[/]");
            return 1;
        }

        using var source = SchemaSourceFactory.Create(settings.Source);

        var packet = SchemaLookupHelper.TryReadPacket(source, settings.Cache, settings.PacketName, settings.Source);
        if (packet is null) return 1;

        var schemaNames = packet.Fields.Select(f => Scaffolding.PropertyNamer.ToPascalCase(f.Name)).ToList();
        var existingNames = ExistingPacketReader.ReadWirePropertyNames(settings.File);

        var added = schemaNames.Except(existingNames).ToList();
        var removed = existingNames.Except(schemaNames).ToList();
        var reordered = schemaNames.SequenceEqual(existingNames) == false &&
                         added.Count == 0 && removed.Count == 0;

        if (added.Count == 0 && removed.Count == 0 && !reordered)
        {
            AnsiConsole.MarkupLine("[green]No differences.[/]");
            return 0;
        }

        if (added.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Fields in schema but not in the existing class:[/]");
            foreach (var f in added) AnsiConsole.MarkupLine($"  [green]+[/] {f}");
        }

        if (removed.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Fields in the existing class but not in schema:[/]");
            foreach (var f in removed) AnsiConsole.MarkupLine($"  [red]-[/] {f}");
        }

        if (reordered)
        {
            AnsiConsole.MarkupLine("[yellow]Same fields, different order:[/]");
            AnsiConsole.MarkupLine($"  schema:   {string.Join(", ", schemaNames)}");
            AnsiConsole.MarkupLine($"  existing: {string.Join(", ", existingNames)}");
        }

        return 1;
    }
}
