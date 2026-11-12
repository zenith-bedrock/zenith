using System.IO.Compression;
using System.Text;
using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>ADR §147: recipes can require and produce item aux values (bed/banner colour, ...).</summary>
public class RecipeAuxTests
{
    private static readonly ItemPalette Palette = ItemPaletteLoader.FromEmbeddedResource();

    private const string Data = """
        {"shaped":[
          {"id":"red_bed","w":3,"h":2,"sym":false,"st":"crafting_table",
           "in":[{"n":"minecraft:red_wool"},{"n":"minecraft:red_wool"},{"n":"minecraft:red_wool"},
                 {"n":"minecraft:oak_planks"},{"n":"minecraft:oak_planks"},{"n":"minecraft:oak_planks"}],
           "out":[{"n":"minecraft:bed","c":1,"a":14}]}],
         "shapeless":[
          {"id":"red_bed_to_arrow","st":"crafting_table",
           "in":[{"n":"minecraft:bed","a":14},{"n":"minecraft:stick"}],
           "out":[{"n":"minecraft:arrow","c":2}]},
          {"id":"any_bed_to_bowl","st":"crafting_table",
           "in":[{"n":"minecraft:bed"},{"n":"minecraft:poppy"}],
           "out":[{"n":"minecraft:bowl","c":1}]}],
         "source":"test"}
        """;

    private static RecipeRegistry Load(string json = Data)
    {
        Blocks.EnsureLoaded();
        using var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(Encoding.UTF8.GetBytes(json));
        gz.Position = 0;
        return RecipeRegistry.FromStream(gz, Palette);
    }

    private static InventorySlot Bed(int aux, int n = 1) => new(StackId.FromItem(Palette.Require("minecraft:bed"), aux), n);
    private static InventorySlot Item(string name, int n = 1) => InventorySlot.OfItem(Palette.Require(name), n);

    [Fact]
    public void A_recipe_output_carries_its_aux()
    {
        var reg = Load();
        var grid = new PlayerTableCraftUi();
        for (var i = 0; i < 3; i++) Assert.True(grid.TrySetGrid(i, Item("minecraft:red_wool")));
        for (var i = 3; i < 6; i++) Assert.True(grid.TrySetGrid(i, InventorySlot.OfBlock(Blocks.OakPlanks, 1)));

        Assert.True(reg.TryCraftFromGrid(grid, reg.NetIdOf("red_bed"), out var output, 1, isAtCraftingTable: true));
        Assert.Equal(StackId.FromItem(Palette.Require("minecraft:bed"), 14), output.Id);
    }

    [Fact]
    public void An_ingredient_that_names_an_aux_matches_only_that_aux()
    {
        var reg = Load();
        var id = reg.NetIdOf("red_bed_to_arrow");

        var red = new PlayerCraftUi();
        Assert.True(red.TrySetGrid(0, Bed(14)));
        Assert.True(red.TrySetGrid(1, Item("minecraft:stick")));
        Assert.True(reg.TryCraftFromGrid(red, id, out var output, 1, isAtCraftingTable: false));
        Assert.Equal(2, output.Count);

        var blue = new PlayerCraftUi();
        Assert.True(blue.TrySetGrid(0, Bed(11)));
        Assert.True(blue.TrySetGrid(1, Item("minecraft:stick")));
        Assert.False(reg.TryCraftFromGrid(blue, id, out _, 1, isAtCraftingTable: false));
    }

    [Fact]
    public void An_ingredient_without_aux_is_a_wildcard()
    {
        var reg = Load();
        var id = reg.NetIdOf("any_bed_to_bowl");
        foreach (var aux in new[] { 0, 3, 14 })
        {
            var grid = new PlayerCraftUi();
            Assert.True(grid.TrySetGrid(0, Bed(aux)));
            Assert.True(grid.TrySetGrid(1, Item("minecraft:poppy")));
            Assert.True(reg.TryCraftFromGrid(grid, id, out _, 1, isAtCraftingTable: false), $"aux {aux}");
        }
    }

    [Fact]
    public void Auto_craft_allocation_respects_ingredient_aux()
    {
        var reg = Load();
        var id = reg.NetIdOf("red_bed_to_arrow");
        InventorySlot[] sources = [Bed(11, 5), Item("minecraft:stick", 5), Bed(14, 1)];

        Assert.True(reg.TryPlanAutoCraft(id, 1, sources, isAtCraftingTable: false, out _, out var takes));
        Assert.Equal([0, 1, 1], takes); // the blue bed is skipped, the red one is taken

        Assert.False(reg.TryPlanAutoCraft(id, 2, sources, isAtCraftingTable: false, out _, out _)); // only one red bed
    }

    [Fact]
    public void CraftingData_sends_the_aux_as_output_metadata_and_ingredient_metadata()
    {
        var reg = Load();
        var packet = CraftingDataBuilder.Build(reg, Palette);

        Assert.Equal(14, packet.ShapedRecipes.Single(r => r.RecipeId == "red_bed").Outputs[0].Meta);
        var arrow = packet.Recipes.Single(r => r.RecipeId == "red_bed_to_arrow");
        Assert.Equal(14, arrow.Inputs[0].Aux);
        Assert.Equal(WireRecipeIngredient.AnyAux, arrow.Inputs[1].Aux);
        Assert.Equal(WireRecipeIngredient.AnyAux, packet.Recipes.Single(r => r.RecipeId == "any_bed_to_bowl").Inputs[0].Aux);
    }

    [Fact]
    public void Recipe_data_with_aux_on_a_name_that_is_not_an_item_is_refused()
    {
        var json = Data.Replace("""{"n":"minecraft:bed","c":1,"a":14}""", """{"n":"minecraft:not_an_item","c":1,"a":3}""");
        Assert.Throws<InvalidDataException>(() => Load(json));
    }
}
