using System.Text.Json.Nodes;

namespace Zenith.BedrockData.Recipes;

/// <summary>
/// A CraftingData capture from a stock BDS used ONLY as an oracle: every aux-free grid recipe it sends must have an
/// equivalent in the emitted set. The capture is never an input to the output.
/// </summary>
internal static class WireOracle
{
    public sealed record Report(int PlainWire, int Missing, int NeedAux, int NeedNbt, List<Recipe> MissingRecipes, List<string> Extra, IReadOnlyList<(string Output, int Count)> AuxByOutput);

    public static Report Verify(string captureDir, IReadOnlyList<Recipe> emitted, IReadOnlyDictionary<int, string> itemNames)
    {
        var (plain, aux, nbt) = Read(Path.Combine(captureDir, "crafting_data.json"), itemNames);
        var ours = emitted.Select(r => RecipeJson.Canonical(r)).ToHashSet(StringComparer.Ordinal);
        var missing = plain.Where(r => !ours.Contains(RecipeJson.Canonical(r))).ToList();
        var wire = plain.Select(r => RecipeJson.Canonical(r)).ToHashSet(StringComparer.Ordinal);
        var extra = emitted.Where(r => !wire.Contains(RecipeJson.Canonical(r))).Select(r => r.Id).ToList();
        var auxByOutput = aux.GroupBy(r => r.Outputs[0].Name).OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count())).ToList();
        return new Report(plain.Count, missing.Count, aux.Count, nbt.Count, missing, extra, auxByOutput);
    }

    /// <summary>Wire grid recipes in emitter shape; ones needing an item aux value are returned separately.</summary>
    private static (List<Recipe> Plain, List<Recipe> Aux, List<Recipe> Nbt) Read(string path, IReadOnlyDictionary<int, string> names)
    {
        var wire = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        List<Recipe> plain = [], aux = [], nbt = [];
        foreach (var kind in new[] { "shaped_recipes", "shapeless_recipes", "shulker_box_recipes" })
        {
            foreach (var node in wire[kind]!.AsArray())
            {
                var r = node!.AsObject();
                if ((string)r["block"]! is not ("crafting_table" or "")) continue;

                var needsAux = false;
                var cells = new List<Ingredient?>();
                foreach (var i in r["input"]!.AsArray())
                {
                    if ((string)i!["type"]! == "invalid") { cells.Add(null); continue; }
                    Ingredient cell;
                    var count = (int)i["count"]!;
                    if ((string)i["descriptor_type"]! == "item_tag") cell = Ingredient.OfTag((string)i["tag"]!, count);
                    else
                    {
                        var name = (string)i["name"]!;
                        var cut = name.LastIndexOf(':');
                        var (head, tail) = cut < 0 ? ("", name) : (name[..cut], name[(cut + 1)..]);
                        int? suffixAux = null;
                        if (tail.Length > 0 && tail.All(char.IsAsciiDigit) && head.Contains(':')) // BDS writes "name:aux" for some ingredients
                        {
                            suffixAux = int.Parse(tail);
                            name = head;
                        }
                        var metadata = (int)i["metadata"]!;
                        if (metadata == Recipe.AnyAux && RecipeTables.LegacyWildcardTags.TryGetValue(name, out var wildcardTag))
                            cell = Ingredient.OfTag(wildcardTag, count);
                        else if (RecipeTables.AuxCarriers.Contains(name)) // a real aux (ADR §147); 32767 is the wildcard
                            cell = Ingredient.Item(name, count, suffixAux ?? (metadata == Recipe.AnyAux ? null : metadata));
                        else
                        {
                            needsAux |= suffixAux is > 0 || (metadata is not (0 or 32767) && !name.EndsWith(":0", StringComparison.Ordinal));
                            cell = Ingredient.Item(name, count);
                        }
                    }
                    cells.Add(cell);
                }

                var outs = new List<Product>();
                foreach (var o in r["output"]!.AsArray())
                {
                    var outName = names[(int)o!["network_id"]!];
                    var outMeta = (int)o["metadata"]!;
                    if (RecipeTables.AuxCarriers.Contains(outName))
                        outs.Add(new Product(outName, (int)o["count"]!, outMeta));
                    else
                    {
                        needsAux |= outMeta != 0;
                        outs.Add(new Product(outName, (int)o["count"]!));
                    }
                }
                needsAux |= cells.Any(c => c?.Name is { } n && RecipeTables.AuxItems.Contains(n) && !RecipeTables.AuxCarriers.Contains(n))
                            || outs.Any(o => RecipeTables.AuxItems.Contains(o.Name) && !RecipeTables.AuxCarriers.Contains(o.Name));

                var shaped = r.ContainsKey("width");
                var recipe = new Recipe
                {
                    Id = (string)r["recipe_id"]!,
                    Width = shaped ? (int)r["width"]! : null,
                    Height = shaped ? (int)r["height"]! : null,
                    Inputs = shaped ? cells : cells.Where(c => c is not null).ToList(),
                    Outputs = outs,
                };
                var needsNbt = cells.Any(c => c?.Name is { } n && RecipeTables.NbtItems.Contains(n))
                               || outs.Any(o => RecipeTables.NbtItems.Contains(o.Name));
                (needsNbt ? nbt : needsAux ? aux : plain).Add(recipe);
            }
        }
        return (plain, aux, nbt);
    }
}
