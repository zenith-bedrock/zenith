using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.ProtocolImport.Schema;

namespace Zenith.ProtocolImport.Commands;

internal sealed class PullSettings : CommandSettings
{
    [CommandOption("--source <NAME>")]
    [Description("Schema provider: endstone (default) or mojang.")]
    public string Source { get; set; } = SchemaSourceFactory.Endstone;

    [CommandOption("--cache <DIR>")]
    [Description("Cache directory. Default: ./.cache")]
    public string Cache { get; set; } = ".cache";

    [CommandOption("--ref <REF>")]
    [Description("Git ref to pull. Defaults to the chosen source's own default branch.")]
    public string? Ref { get; set; }
}

/// <summary>
/// `protocol-import pull` - downloads packet/type/enum JSON from the chosen provider
/// (EndstoneMC/protocol-docs or Mojang/bedrock-protocol-docs) into a local cache. Never runs
/// automatically (not part of build/CI) - see ADR §76.
/// </summary>
internal sealed class PullCommand : AsyncCommand<PullSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PullSettings settings)
    {
        using var source = SchemaSourceFactory.Create(settings.Source);
        var @ref = settings.Ref ?? source.DefaultRef;

        AnsiConsole.MarkupLine($"[grey]Pulling {source.Name}@{@ref} into {settings.Cache} ...[/]");

        await AnsiConsole.Status().StartAsync("Downloading...", async ctx =>
        {
            await source.PullAsync(settings.Cache, @ref, CancellationToken.None);
        });

        var count = source.ListCachedPackets(settings.Cache).Count;
        AnsiConsole.MarkupLine($"[green]Done.[/] {count} packet definitions cached.");
        return 0;
    }
}
