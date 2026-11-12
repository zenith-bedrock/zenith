using System.Text.Json.Nodes;

namespace Zenith.BedrockData.Recipes;

/// <summary>
/// A CraftingData capture from a stock BDS used ONLY as an oracle: every aux-free grid recipe it sends must have an
/// equivalent in the emitted set. The capture is never an input to the output.
/// </summary>
internal static class WireOracle
{
    public sealed record Report(int PlainWire, int Missing, int NeedAux, List<Recipe> MissingRecipes, List<string> Extra);

    public static Report Verify(string captureDir, IReadOnlyList<Recipe> emitted, IReadOnlyDictionary<int, string> itemNames)
    {
        var (plain, aux) = Read(Path.Combine(captureDir, "crafting_data.json"), itemNames);
        var ours = emitted.Select(RecipeJson.Canonical).ToHashSet(StringComparer.Ordinal);
        var missing = plain.Where(r => !ours.Contains(RecipeJson.Canonical(r))).ToList();
        var wire = plain.Select(RecipeJson.Canonical).ToHashSet(StringComparer.Ordinal);
        var extra = emitted.Where(r => !wire.Contains(RecipeJson.Canonical(r))).Select(r => r.Id).ToList();
        return new Report(plain.Count, missing.Count, aux.Count, missing, extra);
    }

    /// <summary>Wire grid recipes in emitter shape; ones needing an item aux value are returned separately.</summary>
    private static (List<Recipe> Plain, List<Recipe> Aux) Read(string path, IReadOnlyDictionary<int, string> names)
    {
        var wire = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        List<Recipe> plain = [], aux = [];
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
                        if (tail.Length > 0 && tail.All(char.IsAsciiDigit) && head.Contains(':')) // BDS writes "name:aux" for some ingredients
                        {
                            needsAux |= tail != "0";
                            name = head;
                        }
                        var metadata = (int)i["metadata"]!;
                        needsAux |= metadata is not (0 or 32767) && !name.EndsWith(":0", StringComparison.Ordinal);
                        cell = Ingredient.Item(name, count);
                    }
                    cells.Add(cell);
                }

                var outs = new List<Product>();
                foreach (var o in r["output"]!.AsArray())
                {
                    needsAux |= (int)o!["metadata"]! != 0;
                    outs.Add(new Product(names[(int)o["network_id"]!], (int)o["count"]!));
                }
                needsAux |= cells.Any(c => c?.Name is { } n && RecipeTables.AuxItems.Contains(n))
                            || outs.Any(o => RecipeTables.AuxItems.Contains(o.Name));

                var shaped = r.ContainsKey("width");
                var recipe = new Recipe
                {
                    Id = (string)r["recipe_id"]!,
                    Width = shaped ? (int)r["width"]! : null,
                    Height = shaped ? (int)r["height"]! : null,
                    Inputs = shaped ? cells : cells.Where(c => c is not null).ToList(),
                    Outputs = outs,
                };
                (needsAux ? aux : plain).Add(recipe);
            }
        }
        return (plain, aux);
    }
}
