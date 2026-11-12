using System.IO.Compression;
using System.Text.Json;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Reads the embedded <c>recipes.json.gz</c> (built from the BDS behaviour packs by <c>bedrock-data recipes</c>) into
/// <see cref="CraftingRecipe"/>s, resolving every item NAME against this build's palettes. The data is trusted for
/// nothing it cannot prove: an unknown name, an out-of-range aux, a cell count that cannot be represented or a
/// duplicate id fails the load instead of producing an inert or silently altered recipe. Net ids are assigned in
/// data order (shaped, then shapeless), so they are stable for a given data file.
/// </summary>
static class RecipeDataReader
{
    public static List<CraftingRecipe> Read(Stream gzipJson, ItemPalette palette)
    {
        using var gz = new GZipStream(gzipJson, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);

        var recipes = new List<CraftingRecipe>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        uint nextNetId = 1;
        foreach (var (kind, shaped) in new[] { ("shaped", true), ("shapeless", false) })
        {
            foreach (var entry in doc.RootElement.GetProperty(kind).EnumerateArray())
            {
                var recipe = ReadRecipe(entry, shaped, nextNetId++, palette);
                if (!ids.Add(recipe.Id)) throw new InvalidDataException($"Recipe data: duplicate id '{recipe.Id}'.");
                recipes.Add(recipe);
            }
        }

        return recipes;
    }

    private static CraftingRecipe ReadRecipe(JsonElement e, bool shaped, uint netId, ItemPalette palette)
    {
        var id = e.GetProperty("id").GetString()!;

        var outputs = e.GetProperty("out").EnumerateArray().Select(o => ReadOutput(id, o, palette)).ToArray();
        if (outputs.Length == 0) throw new InvalidDataException($"Recipe data: '{id}' has no output.");

        var cells = new List<RecipeIngredient?>();
        foreach (var c in e.GetProperty("in").EnumerateArray())
        {
            if (c.ValueKind == JsonValueKind.Null) { cells.Add(null); continue; }
            var (ingredient, count) = ReadIngredient(id, c, palette);
            // One grid cell holds one item per craft, so a counted shapeless ingredient is N separate cells; a shaped
            // cell has no such expansion, so a count there would be silently lost and is refused instead.
            if (shaped && count != 1)
                throw new InvalidDataException($"Recipe data: '{id}' has a shaped cell with count {count}.");
            for (var i = 0; i < count; i++) cells.Add(ingredient);
        }

        var width = shaped ? e.GetProperty("w").GetInt32() : cells.Count;
        var height = shaped ? e.GetProperty("h").GetInt32() : 1;
        if (shaped && cells.Count != width * height)
            throw new InvalidDataException($"Recipe data: '{id}' declares {width}x{height} but has {cells.Count} cells.");

        return new CraftingRecipe(
            netId, id, shaped, width, height,
            shaped && e.GetProperty("sym").GetBoolean(),
            e.GetProperty("st").GetString()!,
            cells.ToArray(), outputs);
    }

    private static (RecipeIngredient Ingredient, int Count) ReadIngredient(string recipeId, JsonElement c, ItemPalette palette)
    {
        var count = c.TryGetProperty("c", out var cnt) ? cnt.GetInt32() : 1;
        var name = c.TryGetProperty("n", out var n) ? n.GetString()! : null;
        var tag = c.TryGetProperty("t", out var t) ? t.GetString() : null;
        if (name is null == tag is null)
            throw new InvalidDataException($"Recipe data: '{recipeId}' has an ingredient that is neither a name nor a tag, or both.");
        if (name is not null && !Blocks.TryGetDefaultRuntimeId(name, out _) && !palette.TryGet(name, out _))
            throw new InvalidDataException($"Recipe data: '{recipeId}' uses unknown item '{name}'.");

        int? aux = c.TryGetProperty("a", out var a) ? a.GetInt32() : null;
        if (aux is not null && name is null)
            throw new InvalidDataException($"Recipe data: '{recipeId}' puts an aux on a tag ingredient.");
        RequireValidAux(recipeId, aux);
        return (new RecipeIngredient(name, tag, 1, aux), count);
    }

    /// <summary>
    /// Outputs are resolved by NAME: an explicit aux marks an ITEM stack even when the name is also a block's (bed,
    /// banner: the item carries its colour in aux and no block runtime id, as on the wire); without one a block name is
    /// its default-state block stack and anything else an item stack.
    /// </summary>
    private static RecipeOutput ReadOutput(string recipeId, JsonElement o, ItemPalette palette)
    {
        var name = o.GetProperty("n").GetString()!;
        var count = o.GetProperty("c").GetInt32();

        if (o.TryGetProperty("a", out var auxElement))
        {
            var aux = auxElement.GetInt32();
            RequireValidAux(recipeId, aux);
            if (!palette.TryGet(name, out var auxItemId))
                throw new InvalidDataException($"Recipe data: '{recipeId}' gives an aux to '{name}', which is not an item.");
            return new RecipeOutput(StackId.FromItem(auxItemId, aux), count);
        }

        if (Blocks.TryGetDefaultRuntimeId(name, out var rid))
            return new RecipeOutput(StackId.FromBlock(rid), count);
        if (palette.TryGet(name, out var itemId))
            return new RecipeOutput(StackId.FromItem(itemId), count);
        throw new InvalidDataException($"Recipe data: '{recipeId}' makes unknown item '{name}'.");
    }

    private static void RequireValidAux(string recipeId, int? aux)
    {
        if (aux is < 0 or > StackId.MaxAux)
            throw new InvalidDataException($"Recipe data: '{recipeId}' has aux {aux} outside 0..{StackId.MaxAux}.");
    }
}
