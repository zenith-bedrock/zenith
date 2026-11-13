using System.ComponentModel;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;
using Zenith.BedrockData.Recipes;
using System.Globalization;

namespace Zenith.BedrockData.Commands;

/// <summary>
/// Builds <c>recipes.json.gz</c> from a stock BDS install's behaviour packs (see <see cref="BdsPack"/>) plus the
/// recipes BDS generates in code. Everything is keyed by item NAME; no numeric id and no wire capture is baked in.
/// <c>item_registry.json</c> (names only) is used to check every name exists.
/// </summary>
internal sealed class RecipesCommand : Command<RecipesCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<bds-root>")]
        [Description("Stock BDS install (contains behavior_packs/).")]
        public required string BdsRoot { get; init; }

        [CommandArgument(1, "<item-registry>")]
        [Description("Decoded ItemRegistry capture (item_registry.json with itemstates).")]
        public required string ItemRegistry { get; init; }

        [CommandArgument(2, "<output>")]
        [Description("Where to write recipes.json.gz.")]
        public required string Output { get; init; }

        [CommandOption("--verify <CAPTURE_DIR>")]
        [Description("Use a CraftingData capture as an oracle: fail if an aux-free wire grid recipe has no equivalent here.")]
        public string? Verify { get; init; }

        [CommandOption("--max-missing <N>")]
        [Description("How many missing recipes to list.")]
        [DefaultValue(40)]
        public int MaxMissing { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var states = JsonNode.Parse(File.ReadAllText(settings.ItemRegistry))!["itemstates"]!.AsArray();
        var registry = states.Select(s => (string)s!["name"]!).ToHashSet(StringComparer.Ordinal);

        var pack = PackRecipes.Load(settings.BdsRoot, registry);
        var recipes = pack.Recipes.Concat(CodeRecipes.Build(registry)).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in recipes)
            if (!seen.Add(r.Id)) throw new InvalidDataException($"duplicate recipe id {r.Id}");

        File.WriteAllBytes(settings.Output, RecipeJson.Write(recipes, "BDS behaviour packs (layered vanilla*)"));
        AnsiConsole.MarkupLineInterpolated(CultureInfo.InvariantCulture, 
            $"shaped={recipes.Count(r => r.IsShaped)} shapeless={recipes.Count(r => !r.IsShaped)} not emitted={string.Join(", ", pack.NotEmitted.Select(kv => $"{kv.Key}={kv.Value}"))}");
        foreach (var (reason, ids) in pack.Details.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            AnsiConsole.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"  {reason}: {ids.Count} recipe(s), e.g. {ids[0]}");

        if (settings.Verify is null) return 0;
        var names = states.ToDictionary(s => (int)s!["runtime_id"]!, s => (string)s!["name"]!);
        var report = WireOracle.Verify(settings.Verify, recipes, names);
        AnsiConsole.MarkupLineInterpolated(CultureInfo.InvariantCulture, 
            $"verify: {report.PlainWire} aux-free wire grid recipes; {report.Missing} have no equivalent here; {report.NeedAux} need an aux model not modelled yet, {report.NeedNbt} need stack NBT (skipped by design)");
        AnsiConsole.MarkupLineInterpolated(CultureInfo.InvariantCulture, 
            $"verify: {report.Extra.Count} emitted recipes have no aux-free equivalent on the wire{(report.Extra.Count > 0 ? ", e.g. " + string.Join(", ", report.Extra.Take(8)) : "")}");
        foreach (var (output, count) in report.AuxByOutput)
            AnsiConsole.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"   aux not modelled: {count} x {output}");
        foreach (var r in report.MissingRecipes.Take(settings.MaxMissing))
            AnsiConsole.MarkupLineInterpolated(CultureInfo.InvariantCulture, $"   missing: {r.Id} {string.Join(",", r.Inputs.Select(c => c?.Name ?? c?.Tag ?? "_"))} -> {string.Join(",", r.Outputs.Select(o => $"{o.Name}x{o.Count}"))}");
        return report.Missing == 0 ? 0 : 1;
    }
}
