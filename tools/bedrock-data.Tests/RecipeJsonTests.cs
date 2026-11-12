using System.IO.Compression;
using System.Text;
using Xunit;
using Zenith.BedrockData.Recipes;

namespace Zenith.BedrockData.Tests;

public sealed class RecipeJsonTests
{
    [Fact]
    public void Canonical_ShapelessIgnoresOrderAndExpandsCounts()
    {
        Recipe Make(params Ingredient[] i) => new() { Id = "x", Inputs = [.. i], Outputs = [new Product("minecraft:o", 1)] };
        var a = Make(Ingredient.Item("minecraft:a", 2), Ingredient.Item("minecraft:b"));
        var b = Make(Ingredient.Item("minecraft:b"), Ingredient.Item("minecraft:a"), Ingredient.Item("minecraft:a"));
        Assert.Equal(RecipeJson.Canonical(a), RecipeJson.Canonical(b));
    }

    [Fact]
    public void Write_IsDeterministicAndKeysItemsByName()
    {
        Recipe[] recipes =
        [
            new() { Id = "s", Width = 1, Height = 2, Inputs = [Ingredient.Item("minecraft:a"), null], Outputs = [new Product("minecraft:o", 3)] },
            new() { Id = "l", Inputs = [Ingredient.OfTag("minecraft:planks", 2)], Outputs = [new Product("minecraft:p", 1)] },
        ];
        var first = RecipeJson.Write(recipes, "test");
        Assert.Equal(first, RecipeJson.Write(recipes, "test"));

        using var z = new GZipStream(new MemoryStream(first), CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        Assert.Equal(
            """{"shaped":[{"h":2,"id":"s","in":[{"n":"minecraft:a"},null],"out":[{"c":3,"n":"minecraft:o"}],"st":"crafting_table","sym":true,"w":1}],"shapeless":[{"id":"l","in":[{"c":2,"t":"minecraft:planks"}],"out":[{"c":1,"n":"minecraft:p"}],"st":"crafting_table"}],"source":"test"}""",
            Encoding.UTF8.GetString(o.ToArray()));
    }
}
