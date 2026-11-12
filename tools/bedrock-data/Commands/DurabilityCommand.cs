using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Zenith.BedrockData.Commands;

/// <summary>Builds <c>item_durability.json</c> from a minecraft-data Bedrock items.json and the embedded item palette.</summary>
internal sealed class DurabilityCommand : Command<DurabilityCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<items>")]
        [Description("minecraft-data data/bedrock/<version>/items.json.")]
        public required string Items { get; init; }

        [CommandArgument(1, "<palette>")]
        [Description("Embedded item_palette.json.")]
        public required string Palette { get; init; }

        [CommandArgument(2, "<output>")]
        public required string Output { get; init; }

        [CommandOption("--source <TEXT>")]
        [Description("Provenance recorded in the output, e.g. 'PrismarineJS/minecraft-data bedrock/1.26.60 (MIT)'.")]
        public required string Source { get; init; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        var source = JsonNode.Parse(File.ReadAllText(s.Items))!.AsArray();
        var palette = JsonNode.Parse(File.ReadAllText(s.Palette))!["items"]!.AsArray();
        var result = Durability.Build(source, palette);

        var doc = new JsonObject
        {
            ["source"] = s.Source,
            ["items"] = new JsonObject(result.Items.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
        };
        File.WriteAllText(s.Output, doc.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");

        AnsiConsole.MarkupLine($"[green]{result.Items.Count}[/] durable items written to {Markup.Escape(s.Output)}.");
        if (result.Omitted.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]{result.Omitted.Count} in the source but not in this palette (omitted):[/] {Markup.Escape(string.Join(", ", result.Omitted))}");
        if (result.IdSkewed.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]{result.IdSkewed.Count} matched by name but with a different numeric id (other game version; NOT written):[/] {Markup.Escape(string.Join(", ", result.IdSkewed))}");
        return 0;
    }
}
