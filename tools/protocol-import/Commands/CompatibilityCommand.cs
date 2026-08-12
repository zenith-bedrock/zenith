using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal class CompatibilitySettings : CommandSettings
{
    [CommandOption("--source <NAME>")] public string Source { get; set; } = SchemaSourceFactory.Endstone;
    [CommandOption("--cache <DIR>")] public string Cache { get; set; } = ".cache";
    [CommandOption("--packets-dir <DIR>")] public string? PacketsDir { get; set; }
    [CommandOption("--protocol <VERSION>")] public string? Protocol { get; set; }
    [CommandOption("--json")] [Description("Emit one machine-readable JSON compatibility report.")]
    public bool Json { get; set; }
}

/// <summary>Compatibility evidence suitable for humans and CI; does not change packet sources.</summary>
internal sealed class CompatibilityCommand : Command<CompatibilitySettings>
{
    public override int Execute(CommandContext context, CompatibilitySettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var report = ProtocolGovernanceReader.Read(
            ProtocolGovernanceReader.ReadProtocolVersion(settings.Protocol), settings.Cache, source, settings.PacketsDir);
        if (settings.Json)
        {
            Console.Out.WriteLine(ProtocolGovernanceJson.Compatibility(report));
            return report.ValidationErrors.Count == 0 ? 0 : 1;
        }
        AnsiConsole.MarkupLine($"[bold]Protocol:[/] {report.Protocol.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Schema coverage:[/] {report.Coverage:P1}");
        AnsiConsole.MarkupLine($"[bold]Generated/manual:[/] {report.GeneratedPackets}/{report.ManualPackets}");
        AnsiConsole.MarkupLine($"[bold]RED/YELLOW:[/] {report.Red.Count}/{report.Yellow.Count}");
        AnsiConsole.MarkupLine($"[bold]Generated synchronized:[/] {(report.GeneratedOutOfDate.Count == 0 ? "yes" : "no")}");
        foreach (var item in report.Red.OrderBy(item => item.Packet).ThenBy(item => item.Field))
            AnsiConsole.MarkupLine($"[red]RED[/] {item.Packet.EscapeMarkup()}.{item.Field.EscapeMarkup()}: {item.Reason.EscapeMarkup()}");
        foreach (var item in report.Yellow.OrderBy(item => item.Packet).ThenBy(item => item.Field))
            AnsiConsole.MarkupLine($"[yellow]YELLOW[/] {item.Packet.EscapeMarkup()}.{item.Field.EscapeMarkup()}: {item.Reason.EscapeMarkup()}");
        foreach (var issue in report.GeneratedOutOfDate)
            AnsiConsole.MarkupLine($"[yellow]OUT OF DATE[/] {issue.Packet.EscapeMarkup()}.{issue.Field.EscapeMarkup()}: {issue.Reason.EscapeMarkup()}");
        foreach (var error in report.ValidationErrors) AnsiConsole.MarkupLine($"[red]INVALID[/] {error.EscapeMarkup()}");
        return report.ValidationErrors.Count == 0 ? 0 : 1;
    }
}
