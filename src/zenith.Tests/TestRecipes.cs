using Zenith.Gameplay.Inventory;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Net ids of well-known recipes. Net ids are assigned in data order, so they are stable for a given
/// <c>recipes.json.gz</c>; tests name recipes by vanilla id and resolve them here once.
/// </summary>
internal static class TestRecipes
{
    private static readonly Lazy<RecipeRegistry> Registry = new(() =>
    {
        Blocks.EnsureLoaded();
        return RecipeRegistry.CreateDefault();
    });

    public static RecipeRegistry Default => Registry.Value;

    public static uint OakLogToPlanks => Default.NetIdOf("minecraft:oak_planks");
    public static uint PlanksToStick => Default.NetIdOf("minecraft:stick");
    public static uint Chest => Default.NetIdOf("minecraft:Chest_recipeId");
    public static uint CraftingTable => Default.NetIdOf("minecraft:crafting_table");
    public static uint WoodenPickaxe => Default.NetIdOf("minecraft:wooden_pickaxe");
}
