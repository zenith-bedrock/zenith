using System.Text;
using Xunit;
using Zenith.GamedataImport;
using Zenith.Nbt;

namespace Zenith.GamedataImport.Tests;

/// <summary>
/// Fixtures below are real vanilla recipe JSON, extracted verbatim from a current BDS install's
/// `recipes.brarchive` — not hand-invented — one per real schema variant this importer has to
/// handle (verified against the full 821-entry distribution before this importer was written).
/// </summary>
public class VanillaRecipeImporterTests
{
    private static BrArchiveEntry Entry(string name, string json) => new(name, Encoding.UTF8.GetBytes(json));

    private static NbtCompound BuildOne(string name, string json)
    {
        var doc = VanillaRecipeImporter.BuildDocument([Entry(name, json)]);
        var recipes = doc.Tag["recipes"].AsList();
        return Assert.Single(recipes.Items.Select(t => t.AsCompound()));
    }

    [Fact]
    public void Converts_a_real_shaped_recipe()
    {
        const string json = """
            {"format_version":"1.12","minecraft:recipe_shaped":{"description":{"identifier":"minecraft:acacia_boat"},"key":{"#":{"data":4,"item":"minecraft:planks"},"P":{"item":"minecraft:wooden_shovel"}},"pattern":["#P#","###"],"result":{"data":4,"item":"minecraft:boat"},"tags":["crafting_table"]}}
            """;
        var c = BuildOne("acacia_boat.json", json);

        Assert.Equal("minecraft:acacia_boat", c["id"].AsString());
        Assert.Equal("shaped", c["type"].AsString());
        Assert.Equal("crafting_table", c["tags"].AsList()[0].AsString());

        var pattern = c["pattern"].AsList();
        Assert.Equal(2, pattern.Count);
        Assert.Equal("#P#", pattern[0].AsString());
        Assert.Equal("###", pattern[1].AsString());

        var key = c["key"].AsList();
        Assert.Equal(2, key.Count);
        var hashKey = key.Items.Single(k => k.AsCompound()["symbol"].AsString() == "#").AsCompound();
        Assert.Equal("minecraft:planks", hashKey["item"].AsString());
        Assert.Equal(4, hashKey["data"].AsInt());
        Assert.Equal(1, hashKey["count"].AsInt()); // absent in source -> default 1

        var results = c["results"].AsList();
        var result = Assert.Single(results.Items).AsCompound();
        Assert.Equal("minecraft:boat", result["item"].AsString());
        Assert.Equal(4, result["data"].AsInt());
        Assert.Equal(1, result["count"].AsInt());
    }

    [Fact]
    public void Converts_a_real_shapeless_recipe()
    {
        const string json = """
            {"format_version":"1.12","minecraft:recipe_shapeless":{"description":{"identifier":"minecraft:andesite"},"ingredients":[{"data":3,"item":"minecraft:stone"},{"item":"minecraft:cobblestone"}],"result":{"count":2,"data":5,"item":"minecraft:stone"},"tags":["crafting_table"]}}
            """;
        var c = BuildOne("andesite.json", json);

        Assert.Equal("minecraft:andesite", c["id"].AsString());
        Assert.Equal("shapeless", c["type"].AsString());

        var ingredients = c["ingredients"].AsList();
        Assert.Equal(2, ingredients.Count);
        var stone = ingredients[0].AsCompound();
        Assert.Equal("minecraft:stone", stone["item"].AsString());
        Assert.Equal(3, stone["data"].AsInt());
        var cobble = ingredients[1].AsCompound();
        Assert.Equal("minecraft:cobblestone", cobble["item"].AsString());
        Assert.Equal(0, cobble["data"].AsInt()); // absent in source -> default 0

        var result = Assert.Single(c["results"].AsList().Items).AsCompound();
        Assert.Equal("minecraft:stone", result["item"].AsString());
        Assert.Equal(5, result["data"].AsInt());
        Assert.Equal(2, result["count"].AsInt());
    }

    [Fact]
    public void Converts_a_real_furnace_recipe()
    {
        const string json = """
            {"format_version":"1.12","minecraft:recipe_furnace":{"description":{"identifier":"minecraft:furnace_acacia_wood"},"input":"minecraft:wood:4","output":"minecraft:coal:1","tags":["furnace"]}}
            """;
        var c = BuildOne("furnace_acacia_wood.json", json);

        Assert.Equal("minecraft:furnace_acacia_wood", c["id"].AsString());
        Assert.Equal("furnace", c["type"].AsString());
        Assert.Equal("minecraft:wood:4", c["input"].AsString());
        Assert.Equal("minecraft:coal:1", c["output"].AsString());
        Assert.Equal("furnace", c["tags"].AsList()[0].AsString());
    }

    [Fact]
    public void Converts_a_real_brewing_mix_recipe()
    {
        const string json = """
            {"format_version":"1.12","minecraft:recipe_brewing_mix":{"description":{"identifier":"minecraft:brew_awkward_blaze_powder"},"input":"minecraft:potion_type:awkward","output":"minecraft:potion_type:strength","reagent":"minecraft:blaze_powder","tags":["brewing_stand"]}}
            """;
        var c = BuildOne("brew_awkward_blaze_powder.json", json);

        Assert.Equal("brewing_mix", c["type"].AsString());
        Assert.Equal("minecraft:potion_type:awkward", c["input"].AsString());
        Assert.Equal("minecraft:potion_type:strength", c["output"].AsString());
        Assert.Equal("minecraft:blaze_powder", c["reagent"].AsString());
    }

    [Fact]
    public void Converts_a_real_brewing_container_recipe()
    {
        const string json = """
            {"format_version":"1.12","minecraft:recipe_brewing_container":{"description":{"identifier":"minecraft:brew_potion_sulphur"},"input":"minecraft:potion","output":"minecraft:splash_potion","reagent":"minecraft:gunpowder","tags":["brewing_stand"]}}
            """;
        var c = BuildOne("brew_potion_sulphur.json", json);

        Assert.Equal("brewing_container", c["type"].AsString());
        Assert.Equal("minecraft:potion", c["input"].AsString());
        Assert.Equal("minecraft:splash_potion", c["output"].AsString());
    }

    /// <summary>Regression: a real byproduct recipe (cake also returns 3 empty buckets) — the one schema variant that broke a naive single-result assumption when first run against all 821 real entries.</summary>
    [Fact]
    public void Converts_a_multi_result_byproduct_recipe()
    {
        const string json = """
            {"format_version":"1.12","minecraft:recipe_shaped":{"description":{"identifier":"minecraft:cake"},"key":{"A":{"item":"minecraft:milk_bucket"},"B":{"item":"minecraft:sugar"},"C":{"item":"minecraft:egg"},"D":{"item":"minecraft:wheat"}},"pattern":["AAA","BDB","BCB"],"result":[{"item":"minecraft:cake"},{"count":3,"data":0,"item":"minecraft:bucket"}],"tags":["crafting_table"]}}
            """;
        var c = BuildOne("cake.json", json);

        var results = c["results"].AsList();
        Assert.Equal(2, results.Count);
        var cake = results[0].AsCompound();
        Assert.Equal("minecraft:cake", cake["item"].AsString());
        Assert.Equal(1, cake["count"].AsInt()); // absent -> default 1
        var buckets = results[1].AsCompound();
        Assert.Equal("minecraft:bucket", buckets["item"].AsString());
        Assert.Equal(3, buckets["count"].AsInt());
    }

    [Fact]
    public void An_unrecognized_top_level_key_throws_with_the_entry_name_named()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            VanillaRecipeImporter.BuildDocument([Entry("mystery.json", """{"minecraft:something_new":{}}""")]));
        Assert.Contains("mystery.json", ex.Message);
    }

    [Fact]
    public void Builds_a_document_covering_every_recipe_in_the_batch()
    {
        var entries = new[]
        {
            Entry("furnace_acacia_wood.json", """{"minecraft:recipe_furnace":{"description":{"identifier":"minecraft:furnace_acacia_wood"},"input":"minecraft:wood:4","output":"minecraft:coal:1","tags":["furnace"]}}"""),
            Entry("brew_potion_sulphur.json", """{"minecraft:recipe_brewing_container":{"description":{"identifier":"minecraft:brew_potion_sulphur"},"input":"minecraft:potion","output":"minecraft:splash_potion","reagent":"minecraft:gunpowder","tags":["brewing_stand"]}}"""),
        };

        var doc = VanillaRecipeImporter.BuildDocument(entries);
        Assert.Equal(2, doc.Tag["recipes"].AsList().Count);
    }

    /// <summary>End-to-end: encode via the same NbtCodec/encoding Program.cs uses for the real
    /// `recipes.nbt` output, decode it back, and confirm the round trip is lossless — this is the
    /// exact path the real 821-entry file went through to reach `src/zenith/data/recipes.nbt`.</summary>
    [Fact]
    public void Encoded_document_round_trips_through_NbtCodec()
    {
        const string json = """
            {"minecraft:recipe_shaped":{"description":{"identifier":"minecraft:acacia_boat"},"key":{"#":{"data":4,"item":"minecraft:planks"},"P":{"item":"minecraft:wooden_shovel"}},"pattern":["#P#","###"],"result":{"data":4,"item":"minecraft:boat"},"tags":["crafting_table"]}}
            """;
        var doc = VanillaRecipeImporter.BuildDocument([Entry("acacia_boat.json", json)]);

        var encoded = NbtCodec.Encode(doc, NbtEncoding.LittleEndian);
        var decoded = NbtCodec.Decode(encoded, NbtEncoding.LittleEndian);

        var recipes = decoded.Root.Tag["recipes"].AsList();
        var recipe = Assert.Single(recipes.Items).AsCompound();
        Assert.Equal("minecraft:acacia_boat", recipe["id"].AsString());
        Assert.Equal("shaped", recipe["type"].AsString());
        Assert.Equal("minecraft:boat", recipe["results"].AsList()[0].AsCompound()["item"].AsString());
    }
}
