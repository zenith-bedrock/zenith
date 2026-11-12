using System.Diagnostics.CodeAnalysis;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Every grid-craftable vanilla recipe (see <see cref="RecipeDataReader"/>) plus the two ways to craft one: from an
/// open crafting grid (<see cref="TryCraftFromGrid"/>) and from a recipe-book request over the grid and the inventory
/// (<see cref="TryPlanAutoCraft"/>). It maps stacks to what <see cref="RecipeMatcher"/> sees; the matching rules live
/// there.
/// </summary>
sealed class RecipeRegistry
{
    public const string EmbeddedLogicalName = "recipes.json.gz";

    private readonly Dictionary<uint, CraftingRecipe> _byNetId = new();
    private readonly Dictionary<string, uint> _byId = new();
    private readonly ItemPalette _palette;

    private RecipeRegistry(ItemPalette palette, IEnumerable<CraftingRecipe> recipes)
    {
        _palette = palette;
        foreach (var recipe in recipes)
        {
            _byNetId[recipe.NetId] = recipe;
            _byId[recipe.Id] = recipe.NetId;
        }
    }

    public static RecipeRegistry CreateDefault() => CreateDefault(ItemPaletteLoader.FromEmbeddedResource());

    public static RecipeRegistry CreateDefault(ItemPalette itemPalette)
    {
        using var stream = typeof(RecipeRegistry).Assembly.GetManifestResourceStream(EmbeddedLogicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedLogicalName}' not found.");
        return FromStream(stream, itemPalette);
    }

    public static RecipeRegistry FromStream(Stream gzipJson, ItemPalette itemPalette)
    {
        Blocks.EnsureLoaded();
        return new RecipeRegistry(itemPalette, RecipeDataReader.Read(gzipJson, itemPalette));
    }

    public int Count => _byNetId.Count;

    public bool TryGetNetId(string recipeId, out uint netId) => _byId.TryGetValue(recipeId, out netId);

    public uint NetIdOf(string recipeId) =>
        _byId.TryGetValue(recipeId, out var id) ? id : throw new KeyNotFoundException($"Unknown recipe '{recipeId}'.");

    public bool TryGet(uint netId, [NotNullWhen(true)] out CraftingRecipe? recipe) => _byNetId.TryGetValue(netId, out recipe);

    /// <summary>All recipes ordered by net id — the SSOT for the CraftingData wire.</summary>
    public IReadOnlyList<CraftingRecipe> Recipes => _byNetId.Values.OrderBy(r => r.NetId).ToList();

    /// <summary>
    /// Whether <paramref name="grid"/> holds exactly this recipe's ingredients (shape-aware for shaped recipes);
    /// on success <paramref name="used"/> lists the grid cells one craft takes one item from.
    /// </summary>
    public bool TryMatch(CraftingRecipe recipe, ICraftGrid grid, out List<int> used)
    {
        var cells = new GridItem?[grid.GridSize];
        for (var i = 0; i < cells.Length; i++) cells[i] = ItemOf(grid.GetGrid(i));

        used = [];
        return RecipeMatcher.TryMatchGrid(recipe, cells, grid.GridSize == 4 ? 2 : 3, used);
    }

    /// <summary>
    /// Match + consume <paramref name="times"/> crafts from <paramref name="grid"/> (0 is read as 1: the wire's craft
    /// count is boilerplate for a plain craft). A request for more crafts than the grid affords, or than one result
    /// stack can hold, is refused, not reduced: the client's own Consume list was computed for the count it asked for.
    /// The grid is restored if anything fails.
    /// </summary>
    public bool TryCraftFromGrid(ICraftGrid grid, uint netId, out InventorySlot output, int times, bool isAtCraftingTable)
    {
        output = InventorySlot.Empty;
        if (times == 0) times = 1;
        if (!TryResolveCraft(netId, times, isAtCraftingTable, out var recipe)) return false;
        if (!TryMatch(recipe, grid, out var used)) return false;

        foreach (var i in used)
            if (grid.GetGrid(i).Count < times) return false;

        var snapshot = grid.CaptureSnapshot();
        foreach (var i in used)
        {
            var slot = grid.GetGrid(i);
            var left = slot.Count - times;
            if (!grid.TrySetGrid(i, left == 0 ? InventorySlot.Empty : slot with { Count = left }))
            {
                grid.RestoreSnapshot(snapshot);
                return false;
            }
        }

        var main = recipe.Outputs[0];
        output = new InventorySlot(main.Stack, main.Count * times);
        return true;
    }

    /// <summary>
    /// Recipe-book crafting: how much of each of <paramref name="sources"/> (the open grid's cells, then the player's
    /// slots, in the order the caller wants them drained) <paramref name="times"/> crafts take; see
    /// <see cref="RecipeMatcher.TryAllocate"/>. Like <see cref="TryCraftFromGrid"/> an unaffordable or over-large
    /// request is refused, never reduced. Nothing is mutated.
    /// </summary>
    public bool TryPlanAutoCraft(
        uint netId,
        int times,
        IReadOnlyList<InventorySlot> sources,
        bool isAtCraftingTable,
        [NotNullWhen(true)] out CraftingRecipe? recipe,
        out int[] takes)
    {
        takes = new int[sources.Count];
        if (!TryResolveCraft(netId, times, isAtCraftingTable, out recipe)) return false;
        var stacks = sources.Select(s => new SourceStack(ItemOf(s), s.Count)).ToArray();
        return RecipeMatcher.TryAllocate(recipe, times, stacks, out takes);
    }

    /// <summary>The checks every craft shares: the recipe exists, fits the open grid, and <paramref name="times"/> results fit one stack.</summary>
    private bool TryResolveCraft(uint netId, int times, bool isAtCraftingTable, [NotNullWhen(true)] out CraftingRecipe? recipe)
    {
        if (!_byNetId.TryGetValue(netId, out recipe) || recipe.Outputs.Length == 0) return false;
        if (recipe.RequiresTable && !isAtCraftingTable) return false;
        if (times <= 0) return false;
        var main = recipe.Outputs[0];
        return main.Count > 0 && times <= PlayerInventory.MaxStack / main.Count;
    }

    /// <summary>Name and aux of whatever sits in a grid cell (blocks and items share the namespace); null when empty.</summary>
    private GridItem? ItemOf(InventorySlot slot)
    {
        if (slot.IsEmpty) return null;
        if (slot.Id.IsBlock) return new GridItem(Blocks.TryGetName(slot.Id.Value, out var b) ? b : "", 0);
        return new GridItem(_palette.TryGetName(slot.Id.Value, out var i) ? i : "", slot.Id.Aux);
    }
}
