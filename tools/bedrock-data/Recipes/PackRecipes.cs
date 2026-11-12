using System.Text.Json.Nodes;

namespace Zenith.BedrockData.Recipes;

/// <summary>Why a pack recipe was not emitted. Counted and reported, never guessed around.</summary>
internal sealed class UnresolvedItem(string kind, string detail) : Exception(detail)
{
    public string Kind { get; } = kind;
}

/// <summary>
/// Crafting-table recipes from the layered <c>recipes</c> catalog of the stock BDS behaviour packs: shaped and
/// shapeless, legacy spellings and <c>data</c> values resolved through <see cref="RecipeTables"/>. Recipes of other
/// stations are other systems' data; recipes needing an item aux value are reported, not emitted.
/// </summary>
internal static class PackRecipes
{
    public sealed record Result(List<Recipe> Recipes, Dictionary<string, int> NotEmitted, Dictionary<string, List<string>> Details);

    public static Result Load(string bdsRoot, IReadOnlySet<string> registry)
    {
        var recipes = new OrderedDictionary<string, Recipe>(StringComparer.Ordinal); // later pack replaces same identifier in place
        var notEmitted = new Dictionary<string, int>();
        var details = new Dictionary<string, List<string>>();
        var order = BdsPack.PackOrder(bdsRoot).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var files = BdsPack.LoadCategory(bdsRoot, "recipes")
            .OrderBy(kv => order[kv.Value.Pack]).ThenBy(kv => kv.Key, StringComparer.Ordinal);

        foreach (var (_, (_, blob)) in files)
        {
            foreach (var (kind, body) in JsonNode.Parse(blob)!.AsObject())
            {
                if (kind is not ("minecraft:recipe_shaped" or "minecraft:recipe_shapeless")) continue;
                var id = (string)body!["description"]!["identifier"]!;
                var tags = body["tags"]?.AsArray().Select(t => (string?)t).ToList() ?? [];
                if (!tags.Contains(RecipeTables.Station)) { Bump(notEmitted, "other_station"); continue; }

                try
                {
                    var results = body["result"] is JsonArray arr ? arr.Select(n => n!.AsObject()).ToList() : [body["result"]!.AsObject()];
                    var outs = results.Select(r =>
                    {
                        var (name, aux) = Resolve(r, registry, isResult: true);
                        return new Product(name, (int?)r["count"] ?? 1, aux);
                    }).ToList();
                    recipes[id] = kind.EndsWith("shaped", StringComparison.Ordinal)
                        ? Shaped(id, body, outs, registry)
                        : new Recipe
                        {
                            Id = id,
                            Inputs = body["ingredients"]!.AsArray().Select(i => (Ingredient?)Ingredient(i!.AsObject(), registry)).ToList(),
                            Outputs = outs,
                        };
                }
                catch (UnresolvedItem e)
                {
                    Bump(notEmitted, "unresolved:" + e.Kind);
                    if (!details.TryGetValue(e.Message, out var ids)) details[e.Message] = ids = [];
                    ids.Add(id);
                }
            }
        }
        return new Result([.. recipes.Values], notEmitted, details);
    }

    private static Recipe Shaped(string id, JsonNode body, List<Product> outs, IReadOnlySet<string> registry)
    {
        var pattern = body["pattern"]!.AsArray().Select(r => (string)r!).ToList();
        var key = body["key"]!.AsObject();
        var width = pattern.Max(r => r.Length);
        var cells = new List<Ingredient?>();
        foreach (var row in pattern)
            foreach (var ch in row.PadRight(width))
                cells.Add(ch == ' ' ? null : Ingredient(key[ch.ToString()]!.AsObject(), registry));
        return new Recipe { Id = id, Width = width, Height = pattern.Count, Symmetric = true, Inputs = cells, Outputs = outs };
    }

    private static Ingredient Ingredient(JsonObject entry, IReadOnlySet<string> registry)
    {
        var count = (int?)entry["count"] ?? 1;
        if (entry["tag"] is { } tag) return Recipes.Ingredient.OfTag((string)tag!, count);
        var (name, aux) = Resolve(entry, registry, isResult: false);
        return Recipes.Ingredient.Item(name, count, aux);
    }

    private static (string Name, int? Aux) Resolve(JsonObject entry, IReadOnlySet<string> registry, bool isResult)
    {
        var name = (string)entry["item"]!;
        var data = (int?)entry["data"] ?? 0;
        if (RecipeTables.Aliases.TryGetValue(name, out var alias)) name = alias;
        if (!name.StartsWith("minecraft:", StringComparison.Ordinal)) name = "minecraft:" + name;
        int? aux = null;
        if (RecipeTables.DataNames.TryGetValue(name, out var table))
        {
            if (data >= table.Length) throw new UnresolvedItem("data", $"{name} data {data}");
            name = table[data];
        }
        else if (RecipeTables.AuxCarriers.Contains(name))
            aux = isResult || entry.ContainsKey("data") ? data : null; // an explicit data pins the variant, even 0
        else if (data != 0 && !RecipeTables.IgnoredData.Contains(name))
            throw new UnresolvedItem("aux", $"aux:{name}");
        if (!registry.Contains(name)) throw new UnresolvedItem("unknown", $"unknown:{name}");
        return (name, aux);
    }

    private static void Bump(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
}
