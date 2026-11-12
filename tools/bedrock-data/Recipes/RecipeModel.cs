using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Zenith.BedrockData.Recipes;

/// <summary>One ingredient: an item by NAME or an item tag, never a numeric id. <see cref="Aux"/> null = any aux.</summary>
internal sealed record Ingredient(string? Name, string? Tag, int Count = 1, int? Aux = null)
{
    public static Ingredient Item(string name, int count = 1, int? aux = null) => new(name, null, count, aux);
    public static Ingredient OfTag(string tag, int count = 1) => new(null, tag, count);
}

/// <summary>A result. A non-null <see cref="Aux"/> marks an ITEM stack with that variant value (ADR §147).</summary>
internal sealed record Product(string Name, int Count, int? Aux = null);

/// <summary>Shaped when <see cref="Width"/> is set (cells row-major, null = empty), shapeless otherwise.</summary>
internal sealed class Recipe
{
    public required string Id { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool Symmetric { get; init; } = true;
    public required List<Ingredient?> Inputs { get; init; }
    public required List<Product> Outputs { get; init; }
    public string Station { get; init; } = RecipeTables.Station;

    public bool IsShaped => Width is not null;
}

/// <summary>Deterministic embedded-asset writer: fixed key order, no whitespace, gzip with zero mtime.</summary>
internal static class RecipeJson
{
    public static byte[] Write(IReadOnlyList<Recipe> recipes, string source)
    {
        using var raw = new MemoryStream();
        using (var w = new Utf8JsonWriter(raw, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            WriteList(w, "shaped", recipes.Where(r => r.IsShaped));
            WriteList(w, "shapeless", recipes.Where(r => !r.IsShaped));
            w.WriteString("source", source);
            w.WriteEndObject();
        }

        using var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw.ToArray());
        return gz.ToArray();
    }

    private static void WriteList(Utf8JsonWriter w, string name, IEnumerable<Recipe> recipes)
    {
        w.WriteStartArray(name);
        foreach (var r in recipes) WriteRecipe(w, r);
        w.WriteEndArray();
    }

    // Keys are written alphabetically: h, id, in, out, st, sym, w.
    private static void WriteRecipe(Utf8JsonWriter w, Recipe r)
    {
        w.WriteStartObject();
        if (r.IsShaped) w.WriteNumber("h", r.Height!.Value);
        w.WriteString("id", r.Id);
        w.WriteStartArray("in");
        foreach (var cell in r.Inputs)
        {
            if (cell is null) { w.WriteNullValue(); continue; }
            w.WriteStartObject();
            if (cell.Aux is { } cellAux) w.WriteNumber("a", cellAux);
            if (cell.Count != 1) w.WriteNumber("c", cell.Count);
            if (cell.Name is not null) w.WriteString("n", cell.Name); else w.WriteString("t", cell.Tag);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("out");
        foreach (var o in r.Outputs)
        {
            w.WriteStartObject();
            if (o.Aux is { } outAux) w.WriteNumber("a", outAux);
            w.WriteNumber("c", o.Count);
            w.WriteString("n", o.Name);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteString("st", r.Station);
        if (r.IsShaped)
        {
            w.WriteBoolean("sym", r.Symmetric);
            w.WriteNumber("w", r.Width!.Value);
        }
        w.WriteEndObject();
    }

    /// <summary>Order- and representation-independent identity: shape (or sorted multiset), ingredients, results.</summary>
    public static string Canonical(Recipe r)
    {
        static string Cell(Ingredient? c) =>
            c is null ? "_" : c.Name is not null ? "n:" + c.Name + (c.Aux is { } a ? $"@{a}" : "") : "t:" + c.Tag;
        var outs = string.Join(",", r.Outputs.Select(o => $"{o.Name}{(o.Aux is { } a ? $"@{a}" : "")}x{o.Count}"));
        if (r.IsShaped)
            return $"S{r.Width}x{r.Height}|{string.Join(",", r.Inputs.Select(Cell))}|{outs}";
        var cells = r.Inputs.Where(c => c is not null)
            .SelectMany(c => Enumerable.Repeat(Cell(c), c!.Count)).Order(StringComparer.Ordinal);
        return $"L|{string.Join(",", cells)}|{outs}";
    }
}
