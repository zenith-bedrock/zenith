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

    /// <summary>
    /// Wire grid recipes in emitter shape, split by what they need that Zenith's stack model can carry: plain ones
    /// (names, tags, carrier aux), ones that need an aux on an item that is not a carrier, and ones that need stack NBT.
    /// </summary>
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
                var cells = r["input"]!.AsArray().Select(i => ReadIngredient(i!.AsObject(), ref needsAux)).ToList();
                var outs = r["output"]!.AsArray().Select(o => ReadProduct(o!.AsObject(), names, ref needsAux)).ToList();

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

    /// <summary>
    /// One wire ingredient. BDS writes a legacy <c>name:aux</c> for some of them; for an aux carrier that suffix (or the
    /// metadata) is the real aux, the wire's 32767 metadata is "any", and a legacy name such as <c>minecraft:planks</c>
    /// with the wildcard stands for its tag. Any other item with an aux sets <paramref name="needsAux"/>.
    /// </summary>
    private static Ingredient? ReadIngredient(JsonObject i, ref bool needsAux)
    {
        if ((string)i["type"]! == "invalid") return null;
        var count = (int)i["count"]!;
        if ((string)i["descriptor_type"]! == "item_tag") return Ingredient.OfTag((string)i["tag"]!, count);

        var name = (string)i["name"]!;
        var cut = name.LastIndexOf(':');
        var (head, tail) = cut < 0 ? ("", name) : (name[..cut], name[(cut + 1)..]);
        int? suffixAux = null;
        if (tail.Length > 0 && tail.All(char.IsAsciiDigit) && head.Contains(':'))
        {
            suffixAux = int.Parse(tail);
            name = head;
        }

        var metadata = (int)i["metadata"]!;
        if (metadata == RecipeTables.WireAnyAux && RecipeTables.LegacyWildcardTags.TryGetValue(name, out var wildcardTag))
            return Ingredient.OfTag(wildcardTag, count);
        if (RecipeTables.AuxCarriers.Contains(name))
            return Ingredient.Item(name, count, suffixAux ?? (metadata == RecipeTables.WireAnyAux ? null : metadata));

        needsAux |= suffixAux is > 0 || (metadata is not (0 or RecipeTables.WireAnyAux) && !name.EndsWith(":0", StringComparison.Ordinal));
        return Ingredient.Item(name, count);
    }

    private static Product ReadProduct(JsonObject o, IReadOnlyDictionary<int, string> names, ref bool needsAux)
    {
        var name = names[(int)o["network_id"]!];
        var count = (int)o["count"]!;
        var metadata = (int)o["metadata"]!;
        if (RecipeTables.AuxCarriers.Contains(name)) return new Product(name, count, metadata);

        needsAux |= metadata != 0;
        return new Product(name, count);
    }
}
