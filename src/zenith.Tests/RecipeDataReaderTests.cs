using System.IO.Compression;
using System.Text;
using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>The recipe data is not trusted: anything the model cannot represent faithfully fails the load.</summary>
public class RecipeDataReaderTests
{
    private static readonly ItemPalette Palette = ItemPaletteLoader.FromEmbeddedResource();

    private static List<CraftingRecipe> Read(string json)
    {
        Blocks.EnsureLoaded();
        using var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(Encoding.UTF8.GetBytes(json));
        gz.Position = 0;
        return RecipeDataReader.Read(gz, Palette);
    }

    private static string Doc(string shaped = "", string shapeless = "") =>
        $$"""{"shaped":[{{shaped}}],"shapeless":[{{shapeless}}],"source":"test"}""";

    private const string Stick = """{"n":"minecraft:stick"}""";
    private const string Out = """[{"n":"minecraft:oak_planks","c":4}]""";

    [Fact]
    public void Net_ids_follow_data_order_shaped_first()
    {
        var recipes = Read(Doc(
            shaped: $$"""{"id":"a","w":1,"h":1,"sym":true,"st":"crafting_table","in":[{{Stick}}],"out":{{Out}}}""",
            shapeless: $$"""{"id":"b","st":"crafting_table","in":[{{Stick}}],"out":{{Out}}}"""));

        Assert.Equal([1u, 2u], recipes.Select(r => r.NetId));
        Assert.Equal(["a", "b"], recipes.Select(r => r.Id));
    }

    [Fact]
    public void A_counted_shapeless_ingredient_expands_to_one_cell_per_item()
    {
        var recipes = Read(Doc(shapeless: $$"""{"id":"b","st":"crafting_table","in":[{"c":3,"n":"minecraft:stick"}],"out":{{Out}}}"""));

        Assert.Equal(3, recipes[0].Cells.Length);
    }

    [Fact]
    public void A_shaped_cell_with_a_count_is_refused_instead_of_losing_it()
    {
        var json = Doc(shaped: $$"""{"id":"a","w":1,"h":1,"sym":true,"st":"crafting_table","in":[{"c":2,"n":"minecraft:stick"}],"out":{{Out}}}""");

        Assert.Throws<InvalidDataException>(() => Read(json));
    }

    [Fact]
    public void Duplicate_ids_unknown_items_and_malformed_ingredients_fail_the_load()
    {
        string Shapeless(string id, string ingredient) =>
            Doc(shapeless: $$"""{"id":"{{id}}","st":"crafting_table","in":[{{ingredient}}],"out":{{Out}}}""");

        var one = $$"""{"id":"d","st":"crafting_table","in":[{{Stick}}],"out":{{Out}}}""";
        Assert.Throws<InvalidDataException>(() => Read(Doc(shapeless: $"{one},{one}")));
        Assert.Throws<InvalidDataException>(() => Read(Shapeless("u", """{"n":"minecraft:not_an_item"}""")));
        Assert.Throws<InvalidDataException>(() => Read(Shapeless("n", "{}")));                                  // neither name nor tag
        Assert.Throws<InvalidDataException>(() => Read(Shapeless("b", """{"n":"minecraft:stick","t":"minecraft:planks"}"""))); // both
        Assert.Throws<InvalidDataException>(() => Read(Shapeless("t", """{"t":"minecraft:planks","a":1}""")));  // aux on a tag
    }
}
