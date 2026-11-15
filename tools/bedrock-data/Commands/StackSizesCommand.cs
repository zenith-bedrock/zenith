using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Zenith.BedrockData.Commands;

internal sealed class StackSizesCommand : Command<StackSizesCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<items>")]
        public required string Items { get; init; }
        [CommandArgument(1, "<output>")]
        public required string Output { get; init; }
        [CommandOption("--source <TEXT>")]
        [Description("Version and license of the minecraft-data Bedrock items.json source.")]
        public required string Source { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var bytes = File.ReadAllBytes(settings.Items);
        var rows = StackSizes.Build(JsonNode.Parse(bytes)!.AsArray());
        var doc = new JsonObject
        {
            ["source"] = settings.Source,
            ["source_sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["default_stack_size"] = 64,
            ["items"] = new JsonObject(rows.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)))
        };
        File.WriteAllText(settings.Output, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        AnsiConsole.MarkupLine($"[green]{rows.Count}[/] capacity exceptions written to {Markup.Escape(settings.Output)}.");
        return 0;
    }
}
