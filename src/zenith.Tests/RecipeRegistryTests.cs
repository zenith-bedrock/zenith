using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Shape-, name- and tag-aware matching against the real vanilla recipe data. Each case is something an
/// aggregate-count matcher gets wrong (the previous implementation).
/// </summary>
public class RecipeRegistryTests
{
    private static readonly RecipeRegistry Reg = TestRecipes.Default;
    private static readonly ItemPalette Palette = ItemPaletteLoader.FromEmbeddedResource();

    private static InventorySlot Block(int rid, int n = 1) => InventorySlot.OfBlock(rid, n);
    private static InventorySlot Item(string name, int n = 1) => InventorySlot.OfItem(Palette.Require(name), n);

    private static PlayerTableCraftUi Table(params (int cell, InventorySlot slot)[] cells)
    {
        var grid = new PlayerTableCraftUi();
        foreach (var (cell, slot) in cells) Assert.True(grid.TrySetGrid(cell, slot));
        return grid;
    }

    private static PlayerCraftUi Personal(params (int cell, InventorySlot slot)[] cells)
    {
        var grid = new PlayerCraftUi();
        foreach (var (cell, slot) in cells) Assert.True(grid.TrySetGrid(cell, slot));
        return grid;
    }

    [Fact]
    public void Loads_the_full_vanilla_grid_recipe_set()
    {
        Assert.True(Reg.Count > 3000, $"only {Reg.Count} recipes loaded");
        Assert.True(Reg.TryGetNetId("minecraft:stick", out _));
        Assert.All(Reg.Recipes, r => Assert.NotEmpty(r.Outputs));
    }

    [Fact]
    public void Log_to_planks_crafts_in_the_personal_grid_in_any_cell()
    {
        foreach (var cell in new[] { 0, 1, 2, 3 })
        {
            var grid = Personal((cell, Block(Blocks.OakLog)));
            Assert.True(Reg.TryCraftFromGrid(grid, TestRecipes.OakLogToPlanks, out var output, 1, isAtCraftingTable: false));
            Assert.Equal(StackId.FromBlock(Blocks.OakPlanks), output.Id);
            Assert.Equal(4, output.Count);
        }
    }

    [Fact]
    public void Chest_needs_the_ring_of_eight_planks_on_a_table()
    {
        var ring = new[] { 0, 1, 2, 3, 5, 6, 7, 8 }.Select(i => (i, Block(Blocks.OakPlanks))).ToArray();
        Assert.True(Reg.TryCraftFromGrid(Table(ring), TestRecipes.Chest, out var output, 1, isAtCraftingTable: true));
        Assert.True(output.Id.IsBlock && Blocks.IsChest(output.Id.Value));
    }

    [Fact]
    public void Chest_is_refused_for_eight_planks_in_the_wrong_arrangement()
    {
        // Same eight planks, but the hole is not in the centre: an aggregate-count matcher accepts this.
        var wrong = new[] { 0, 1, 2, 3, 4, 5, 6, 7 }.Select(i => (i, Block(Blocks.OakPlanks))).ToArray();
        Assert.False(Reg.TryCraftFromGrid(Table(wrong), TestRecipes.Chest, out _, 1, isAtCraftingTable: true));
    }

    [Fact]
    public void Chest_is_refused_in_the_personal_grid()
    {
        var grid = Personal(Enumerable.Range(0, 4).Select(i => (i, Block(Blocks.OakPlanks, 2))).ToArray());
        Assert.False(Reg.TryCraftFromGrid(grid, TestRecipes.Chest, out _, 1, isAtCraftingTable: false));
    }

    [Fact]
    public void Extra_items_in_the_grid_refuse_the_craft()
    {
        var grid = Personal((0, Block(Blocks.OakLog)), (3, Block(Blocks.Stone)));
        Assert.False(Reg.TryCraftFromGrid(grid, TestRecipes.OakLogToPlanks, out _, 1, isAtCraftingTable: false));
        Assert.Equal(1, grid.GetGrid(0).Count);
    }

    [Fact]
    public void Tag_ingredient_accepts_any_plank_and_stick_fits_the_personal_grid_anywhere()
    {
        var grid = Personal((1, Block(Blocks.OakPlanks)), (3, Block(Blocks.OakPlanks)));
        Assert.True(Reg.TryCraftFromGrid(grid, TestRecipes.PlanksToStick, out var output, 1, isAtCraftingTable: false));
        Assert.Equal(4, output.Count);
        Assert.True(grid.GetGrid(1).IsEmpty);
        Assert.True(grid.GetGrid(3).IsEmpty);
    }

    [Fact]
    public void Stick_is_refused_when_the_two_planks_are_side_by_side()
    {
        var grid = Personal((0, Block(Blocks.OakPlanks)), (1, Block(Blocks.OakPlanks)));
        Assert.False(Reg.TryCraftFromGrid(grid, TestRecipes.PlanksToStick, out _, 1, isAtCraftingTable: false));
    }

    [Fact]
    public void Shaped_recipe_is_position_independent_on_the_table_but_needs_every_ingredient()
    {
        var stick = Item("minecraft:stick");
        var planks = Block(Blocks.OakPlanks);

        // Wooden pickaxe: planks across the top, two sticks down the middle column.
        var missingStick = Table((0, planks), (1, planks), (2, planks), (4, stick));
        Assert.False(Reg.TryCraftFromGrid(missingStick, TestRecipes.WoodenPickaxe, out _, 1, isAtCraftingTable: true));

        var full = Table((0, planks), (1, planks), (2, planks), (4, stick), (7, stick));
        Assert.True(Reg.TryCraftFromGrid(full, TestRecipes.WoodenPickaxe, out var output, 1, isAtCraftingTable: true));
        Assert.Equal(1, output.Count);
        Assert.True(full.GetGrid(0).IsEmpty);
    }

    [Fact]
    public void Shapeless_matching_is_not_fooled_by_tag_and_name_overlap()
    {
        // Two ingredients where one is a tag the other name also satisfies: assignment must backtrack.
        var coal = Item("minecraft:coal");
        var stick = Item("minecraft:stick");
        Assert.True(Reg.TryGetNetId("minecraft:torch", out var torch));
        var grid = Personal((0, coal), (2, stick));
        Assert.True(Reg.TryCraftFromGrid(grid, torch, out var output, 1, isAtCraftingTable: false));
        Assert.Equal(4, output.Count);
    }

    [Fact]
    public void Crafting_more_than_the_grid_affords_is_refused_and_leaves_the_grid_untouched()
    {
        var grid = Personal((0, Block(Blocks.OakLog, 2)));
        Assert.False(Reg.TryCraftFromGrid(grid, TestRecipes.OakLogToPlanks, out _, 3, isAtCraftingTable: false));
        Assert.Equal(2, grid.GetGrid(0).Count);
        Assert.True(Reg.TryCraftFromGrid(grid, TestRecipes.OakLogToPlanks, out var output, 2, isAtCraftingTable: false));
        Assert.Equal(8, output.Count);
        Assert.True(grid.GetGrid(0).IsEmpty);
    }

    [Fact]
    public void Unknown_recipe_net_id_is_refused()
    {
        Assert.False(Reg.TryCraftFromGrid(Personal((0, Block(Blocks.OakLog))), 0xFFFFFF, out _, 1, isAtCraftingTable: true));
    }

    [Fact]
    public void Tags_cover_every_tag_the_data_references()
    {
        var used = new HashSet<string>();
        foreach (var r in Reg.Recipes)
            foreach (var c in r.Cells)
                if (c?.Tag is { } t) used.Add(t);

        foreach (var tag in used)
        {
            var anyMember = Palette.Entries.Any(e => ItemTags.Contains(tag, e.Name));
            Assert.True(anyMember, $"tag {tag} has no member in the item palette");
        }
    }

    [Fact]
    public void Dyeing_a_wool_block_works_with_real_flattened_names()
    {
        Assert.True(Reg.TryGetNetId("dye_wool_red_from_blue_wool_with_red_dye", out var id));
        var grid = Personal((0, Item("minecraft:red_dye")), (1, Block(Blocks.TryGetDefaultRuntimeId("minecraft:blue_wool", out var blue) ? blue : -1)));
        Assert.True(Reg.TryCraftFromGrid(grid, id, out var output, 1, isAtCraftingTable: false));
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:red_wool", out var red));
        Assert.Equal(StackId.FromBlock(red), output.Id);
    }

    [Fact]
    public void Slab_recipe_that_bds_registers_in_code_is_present()
    {
        Assert.True(Reg.TryGetNetId("cobblestone_slab_from_cobblestone", out var id));
        Assert.True(Reg.TryGet(id, out var recipe));
        Assert.Equal((3, 1), (recipe.Width, recipe.Height));
        Assert.Equal(6, recipe.Outputs[0].Count);
    }

    [Fact]
    public void Recipe_data_naming_an_unknown_item_fails_the_load()
    {
        var json = "{\"shaped\":[],\"shapeless\":[{\"id\":\"t\",\"st\":\"crafting_table\",\"in\":[{\"n\":\"minecraft:not_an_item\"}],\"out\":[{\"n\":\"minecraft:stick\",\"c\":1}]}]}";
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
            gz.Write(System.Text.Encoding.UTF8.GetBytes(json));
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => RecipeRegistry.FromStream(ms, Palette));
    }
}
