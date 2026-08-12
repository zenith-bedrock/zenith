using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class ValidateSettings : CompatibilitySettings { }

/// <summary>Quality gate: verified snapshot plus synchronized generated scalar packet contract.</summary>
internal sealed class ValidateCommand : Command<ValidateSettings>
{
    public override int Execute(CommandContext context, ValidateSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var report = ProtocolGovernanceReader.Read(
            ProtocolGovernanceReader.ReadProtocolVersion(settings.Protocol), settings.Cache, source, settings.PacketsDir);
        var valid = report.ValidationErrors.Count == 0 && report.GeneratedOutOfDate.Count == 0;
        if (settings.Json)
        {
            Console.Out.WriteLine(ProtocolGovernanceJson.Validation(report, valid));
        }
        else if (valid) AnsiConsole.MarkupLine("[green]Protocol import validation passed.[/]");
        else
        {
            foreach (var error in report.ValidationErrors) AnsiConsole.MarkupLine($"[red]INVALID[/] {error.EscapeMarkup()}");
            foreach (var issue in report.GeneratedOutOfDate)
                AnsiConsole.MarkupLine($"[red]OUT OF DATE[/] {issue.Packet.EscapeMarkup()}.{issue.Field.EscapeMarkup()}: {issue.Reason.EscapeMarkup()}");
        }
        return valid ? 0 : 1;
    }
}
