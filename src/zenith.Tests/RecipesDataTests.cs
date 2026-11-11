using System.IO.Compression;
using System.Reflection;
using Xunit;
using Zenith.Nbt;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Proves the `recipes.nbt` embedded resource — real vanilla recipe data, imported from a current
/// BDS install's `recipes.brarchive` by `tools/gamedata-import` (see that tool's own tests for the
/// import/conversion logic) — is actually present in the shipped assembly and decodes correctly.
/// Not yet consumed by any gameplay system (RecipeRegistry stays hand-curated/shapeless — see
/// ADR §142's non-goals); this is a pipeline integrity check, not a gameplay test.
/// </summary>
public class RecipesDataTests
{
    private static NbtNamedTag Load()
    {
        using var stream = typeof(Blocks).Assembly.GetManifestResourceStream("recipes.nbt")
            ?? throw new InvalidOperationException("Embedded resource 'recipes.nbt' not found.");
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        gz.CopyTo(raw);
        return NbtCodec.Decode(raw.ToArray(), NbtEncoding.LittleEndian).Root;
    }

    [Fact]
    public void Embedded_resource_exists_and_decodes()
    {
        var root = Load();
        Assert.True(root.Tag.TryGetCompound(out var compound));
        Assert.True(compound.TryGet("recipes", out var recipesTag));
        Assert.True(recipesTag.TryGetList(out var recipes));
        Assert.True(recipes.Count > 800); // real vanilla data: 821 recipes at import time
    }

    [Fact]
    public void Contains_a_known_real_shaped_recipe_matching_the_source_JSON()
    {
        var root = Load();
        var recipes = root.Tag["recipes"].AsList();

        var acaciaBoat = recipes.Items
            .Select(t => t.AsCompound())
            .Single(c => c["id"].AsString() == "minecraft:acacia_boat");

        Assert.Equal("shaped", acaciaBoat["type"].AsString());
        Assert.Equal("crafting_table", acaciaBoat["tags"].AsList()[0].AsString());
        var result = acaciaBoat["results"].AsList()[0].AsCompound();
        Assert.Equal("minecraft:boat", result["item"].AsString());
        Assert.Equal(4, result["data"].AsInt());
    }

    [Fact]
    public void Covers_every_real_recipe_type_found_at_import_time()
    {
        var root = Load();
        var types = root.Tag["recipes"].AsList().Items
            .Select(t => t.AsCompound()["type"].AsString())
            .ToHashSet();

        Assert.Contains("shaped", types);
        Assert.Contains("shapeless", types);
        Assert.Contains("furnace", types);
        Assert.Contains("brewing_mix", types);
        Assert.Contains("brewing_container", types);
    }
}
