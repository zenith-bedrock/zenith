using Zenith.Packets;
using Zenith.World;
using Zenith.Gameplay.Inventory;

namespace Zenith.Protocol;

/// <summary>Builds CraftingDataPacket from the <see cref="RecipeRegistry"/> SSOT (ADR §35 / §54 / §55).</summary>
static class CraftingDataBuilder
{
    /// <summary>
    /// Wire DTOs straight from the registry — no second recipe table. The station (<c>crafting_table</c>) is sent as the
    /// data has it: whether a recipe fits the personal 2×2 grid is the client's call from its shape, exactly as with a
    /// stock BDS.
    /// </summary>
    public static CraftingDataPacket Build(RecipeRegistry registry, ItemPalette palette)
    {
        var shaped = new List<ShapedCraftingRecipe>();
        var shapeless = new List<ShapelessCraftingRecipe>();
        foreach (var recipe in registry.Recipes)
        {
            var inputs = new WireRecipeIngredient[recipe.Cells.Length];
            for (var i = 0; i < inputs.Length; i++)
                inputs[i] = recipe.Cells[i] is { } c ? new WireRecipeIngredient(c.Name, c.Tag, c.Count) : WireRecipeIngredient.Empty;

            var outputs = new NetworkItemStack[recipe.Outputs.Length];
            for (var i = 0; i < outputs.Length; i++)
            {
                var (stack, count) = recipe.Outputs[i];
                var outName = ResolveName(stack, palette, recipe.NetId);
                outputs[i] = new NetworkItemStack(palette.Require(outName), (ushort)count, stack.IsBlock ? stack.Value : 0);
            }

            if (recipe.Shaped)
                shaped.Add(new ShapedCraftingRecipe
                {
                    RecipeId = recipe.Id, Inputs = inputs, Outputs = outputs, Block = recipe.Station,
                    RecipeNetworkId = recipe.NetId, Width = recipe.Width, Height = recipe.Height,
                    AssumeSymmetry = recipe.AssumeSymmetry
                });
            else
                shapeless.Add(new ShapelessCraftingRecipe
                {
                    RecipeId = recipe.Id, Inputs = inputs, Outputs = outputs, Block = recipe.Station,
                    RecipeNetworkId = recipe.NetId
                });
        }

        return new CraftingDataPacket
        {
            ShapedRecipes = shaped.ToArray(),
            Recipes = shapeless.ToArray(),
            ClearRecipes = true
        };
    }

    private static string ResolveName(StackId id, ItemPalette palette, uint recipeNetId)
    {
        if (id.IsBlock)
        {
            if (!Blocks.TryGetName(id.Value, out var name))
                throw new InvalidOperationException(
                    $"CraftingData: unknown output BlockRuntimeId {id.Value} (recipe {recipeNetId}).");
            return name;
        }

        if (!palette.TryGetName(id.Value, out var itemName))
            throw new InvalidOperationException(
                $"CraftingData: unknown output ItemNetworkId {id.Value} (recipe {recipeNetId}).");
        return itemName;
    }
}
