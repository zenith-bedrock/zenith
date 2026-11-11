using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>One ingredient: a concrete item name or an item tag. Count is how many items one craft takes.</summary>
readonly record struct RecipeIngredient(string? Name, string? Tag, int Count = 1)
{
    public bool Matches(string itemName) =>
        Name is not null ? Name == itemName : Tag is not null && ItemTags.Contains(Tag, itemName);
}

/// <summary>What one craft yields (block outputs keep their default-state runtime id on <see cref="Stack"/>).</summary>
readonly record struct RecipeOutput(StackId Stack, int Count);

/// <summary>
/// One crafting-grid recipe as the wire describes it. <see cref="Cells"/> is row-major
/// <c>Width × Height</c> for shaped recipes (null = must be empty) and the unordered ingredient list for
/// shapeless ones. <see cref="NetId"/> is the id the client sends back in CraftRecipe.
/// </summary>
sealed record CraftingRecipe(
    uint NetId,
    string Id,
    bool Shaped,
    int Width,
    int Height,
    bool AssumeSymmetry,
    string Station,
    RecipeIngredient?[] Cells,
    RecipeOutput[] Outputs)
{
    /// <summary>Whether the recipe cannot fit the personal 2×2 grid (shaped bounding box or shapeless ingredient count).</summary>
    public bool RequiresTable => Shaped ? Width > 2 || Height > 2 : Cells.Length > 4;
}
