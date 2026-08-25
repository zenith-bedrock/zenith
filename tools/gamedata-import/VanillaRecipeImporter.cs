using System.Text.Json;
using Zenith.Nbt;

namespace Zenith.GamedataImport;

/// <summary>
/// Converts unpacked `recipes.brarchive` entries (plain vanilla behavior-pack recipe JSON — see
/// <see cref="BrArchiveReader"/>'s doc comment) into one compact gzip-compressed NBT document,
/// mirroring `block_palette.nbt`'s existing convention (same codec, same compression, same
/// "embedded resource under `src/zenith/data/`" placement) instead of shipping raw JSON.
///
/// Schema verified against the real distribution across all 821 entries of a current BDS install
/// (zero JSON parse failures): `minecraft:recipe_shaped` (456), `minecraft:recipe_shapeless` (207),
/// `minecraft:recipe_furnace` (95), `minecraft:recipe_brewing_mix` (61),
/// `minecraft:recipe_brewing_container` (2). Every field below was read from real samples, not
/// guessed from documentation.
/// </summary>
internal static class VanillaRecipeImporter
{
    public static NbtNamedTag BuildDocument(IReadOnlyList<BrArchiveEntry> entries)
    {
        var recipes = new NbtList(NbtType.Compound);
        var skipped = new List<string>();

        foreach (var entry in entries)
        {
            var recipe = TryConvert(entry, out var reason);
            if (recipe is null)
            {
                skipped.Add($"{entry.Name}: {reason}");
                continue;
            }

            recipes.Add(NbtTag.Compound(recipe));
        }

        if (skipped.Count > 0)
            throw new InvalidDataException(
                $"{skipped.Count} of {entries.Count} recipe entries did not match a known schema:\n" +
                string.Join('\n', skipped));

        var root = new NbtCompound();
        root.Set("recipes", NbtTag.List(recipes));
        return new NbtNamedTag("", NbtTag.Compound(root));
    }

    private static NbtCompound? TryConvert(BrArchiveEntry entry, out string reason)
    {
        using var doc = JsonDocument.Parse(entry.Content);
        var root = doc.RootElement;

        if (root.TryGetProperty("minecraft:recipe_shaped", out var shaped))
            return ConvertShaped(shaped, out reason);
        if (root.TryGetProperty("minecraft:recipe_shapeless", out var shapeless))
            return ConvertShapeless(shapeless, out reason);
        if (root.TryGetProperty("minecraft:recipe_furnace", out var furnace))
            return ConvertFurnace(furnace, out reason);
        if (root.TryGetProperty("minecraft:recipe_brewing_mix", out var brewMix))
            return ConvertBrewing("brewing_mix", brewMix, out reason);
        if (root.TryGetProperty("minecraft:recipe_brewing_container", out var brewContainer))
            return ConvertBrewing("brewing_container", brewContainer, out reason);

        reason = "no known minecraft:recipe_* key at the top level";
        return null;
    }

    private static NbtCompound ConvertShaped(JsonElement e, out string reason)
    {
        var c = NewRecipe("shaped", e);

        var pattern = new NbtList(NbtType.String);
        foreach (var row in e.GetProperty("pattern").EnumerateArray())
            pattern.Add(NbtTag.String(row.GetString()!));
        c.Set("pattern", NbtTag.List(pattern));

        var key = new NbtList(NbtType.Compound);
        foreach (var prop in e.GetProperty("key").EnumerateObject())
        {
            var entryC = new NbtCompound();
            entryC.Set("symbol", NbtTag.String(prop.Name));
            SetIngredient(entryC, prop.Value);
            key.Add(NbtTag.Compound(entryC));
        }

        c.Set("key", NbtTag.List(key));
        c.Set("results", NbtTag.List(BuildResults(e.GetProperty("result"))));

        reason = "";
        return c;
    }

    private static NbtCompound ConvertShapeless(JsonElement e, out string reason)
    {
        var c = NewRecipe("shapeless", e);

        var ingredients = new NbtList(NbtType.Compound);
        foreach (var ing in e.GetProperty("ingredients").EnumerateArray())
        {
            var entryC = new NbtCompound();
            SetIngredient(entryC, ing);
            ingredients.Add(NbtTag.Compound(entryC));
        }

        c.Set("ingredients", NbtTag.List(ingredients));
        c.Set("results", NbtTag.List(BuildResults(e.GetProperty("result"))));

        reason = "";
        return c;
    }

    private static NbtCompound ConvertFurnace(JsonElement e, out string reason)
    {
        var c = NewRecipe("furnace", e);
        c.Set("input", NbtTag.String(InputItemName(e.GetProperty("input"))));
        c.Set("output", NbtTag.String(OutputItemName(e.GetProperty("output"))));
        reason = "";
        return c;
    }

    private static NbtCompound ConvertBrewing(string kind, JsonElement e, out string reason)
    {
        var c = NewRecipe(kind, e);
        c.Set("input", NbtTag.String(e.GetProperty("input").GetString()!));
        c.Set("output", NbtTag.String(e.GetProperty("output").GetString()!));
        c.Set("reagent", NbtTag.String(e.GetProperty("reagent").GetString()!));
        reason = "";
        return c;
    }

    private static NbtCompound NewRecipe(string type, JsonElement e)
    {
        var c = new NbtCompound();
        c.Set("id", NbtTag.String(e.GetProperty("description").GetProperty("identifier").GetString()!));
        c.Set("type", NbtTag.String(type));

        var tags = new NbtList(NbtType.String);
        if (e.TryGetProperty("tags", out var tagsEl))
            foreach (var tag in tagsEl.EnumerateArray())
                tags.Add(NbtTag.String(tag.GetString()!));
        c.Set("tags", NbtTag.List(tags));

        return c;
    }

    /// <summary>
    /// `input`/`output` on furnace recipes are either a plain item name string or `"name:data"` —
    /// both observed in the real data (e.g. `"minecraft:wood:4"`, `"minecraft:coal:1"`).
    /// </summary>
    private static string InputItemName(JsonElement e) => e.GetString()!;

    private static string OutputItemName(JsonElement e) => e.GetString()!;

    /// <summary>Ingredient shape: `{"item": "...", "data": N?, "count": N?}` — data/count optional, both observed absent in real samples.</summary>
    private static void SetIngredient(NbtCompound c, JsonElement e)
    {
        c.Set("item", NbtTag.String(e.GetProperty("item").GetString()!));
        c.Set("data", NbtTag.Int(e.TryGetProperty("data", out var data) ? data.GetInt32() : 0));
        c.Set("count", NbtTag.Int(e.TryGetProperty("count", out var count) ? count.GetInt32() : 1));
    }

    /// <summary>
    /// `result` on a shaped/shapeless recipe is one of three real shapes: a bare item name string,
    /// a single `{"item", "data"?, "count"?}` object, or (byproduct recipes — e.g. cake also
    /// returns 3 empty buckets) an array of that same object shape. Always normalized to a list so
    /// every recipe's actual output count is representable, not just its primary result.
    /// </summary>
    private static NbtList BuildResults(JsonElement e)
    {
        var results = new NbtList(NbtType.Compound);
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
                results.Add(NbtTag.Compound(BuildResultEntry(item)));
        }
        else
        {
            results.Add(NbtTag.Compound(BuildResultEntry(e)));
        }

        return results;
    }

    private static NbtCompound BuildResultEntry(JsonElement e)
    {
        var c = new NbtCompound();
        if (e.ValueKind == JsonValueKind.String)
        {
            c.Set("item", NbtTag.String(e.GetString()!));
            c.Set("data", NbtTag.Int(0));
            c.Set("count", NbtTag.Int(1));
            return c;
        }

        c.Set("item", NbtTag.String(e.GetProperty("item").GetString()!));
        c.Set("data", NbtTag.Int(e.TryGetProperty("data", out var data) ? data.GetInt32() : 0));
        c.Set("count", NbtTag.Int(e.TryGetProperty("count", out var count) ? count.GetInt32() : 1));
        return c;
    }
}
