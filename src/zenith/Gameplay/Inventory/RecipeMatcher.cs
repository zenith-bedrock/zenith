namespace Zenith.Gameplay.Inventory;

/// <summary>One stack as a recipe sees it: what it is (null when empty) and how many items it holds.</summary>
readonly record struct SourceStack(GridItem? Item, int Count);

/// <summary>
/// The recipe-matching rules, free of palettes, players and mutation: a shaped recipe fits its bounding box (optionally
/// mirrored), a shapeless one takes exactly its ingredients in any cell, and recipe-book allocation drains an ordered
/// list of sources. Ingredients match by item name (and aux when pinned) or by item tag — never by aggregate counts.
/// </summary>
static class RecipeMatcher
{
    /// <summary>
    /// Whether <paramref name="cells"/> (a <paramref name="side"/>×<paramref name="side"/> grid, row-major) holds exactly
    /// <paramref name="recipe"/>'s ingredients; on success <paramref name="used"/> lists the cells one craft takes one
    /// item from.
    /// </summary>
    public static bool TryMatchGrid(CraftingRecipe recipe, IReadOnlyList<GridItem?> cells, int side, List<int> used)
    {
        used.Clear();
        return recipe.Shaped ? MatchShaped(recipe, cells, side, used) : MatchShapeless(recipe, cells, used);
    }

    /// <summary>
    /// Recipe-book allocation: how much of each source <paramref name="times"/> crafts take, position-free (every
    /// ingredient needs <c>Count × times</c> matching items). Ingredients that name an item (exact aux first) are served
    /// before tag ingredients so a tag cannot eat the only exact item a later ingredient needs. Greedy, not an optimal
    /// assignment: two tag ingredients with partly overlapping membership may be refused although a split exists.
    /// </summary>
    public static bool TryAllocate(CraftingRecipe recipe, int times, IReadOnlyList<SourceStack> sources, out int[] takes)
    {
        takes = new int[sources.Count];
        foreach (var ingredient in recipe.Cells.OfType<RecipeIngredient>().OrderBy(c => c.Name is null).ThenBy(c => c.Aux is null))
        {
            var need = ingredient.Count * times;
            for (var i = 0; i < sources.Count && need > 0; i++)
            {
                if (sources[i].Item is not { Name.Length: > 0 } item || !ingredient.Matches(item)) continue;
                var take = Math.Min(need, sources[i].Count - takes[i]);
                takes[i] += take;
                need -= take;
            }

            if (need > 0)
            {
                takes = new int[sources.Count];
                return false;
            }
        }

        return true;
    }

    private static bool MatchShaped(CraftingRecipe recipe, IReadOnlyList<GridItem?> cells, int side, List<int> used)
    {
        int minX = side, minY = side, maxX = -1, maxY = -1;
        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i] is null) continue;
            var x = i % side; var y = i / side;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }

        if (maxX < 0 || maxX - minX + 1 != recipe.Width || maxY - minY + 1 != recipe.Height) return false;

        foreach (var mirror in recipe.AssumeSymmetry ? new[] { false, true } : new[] { false })
        {
            used.Clear();
            var ok = true;
            for (var y = 0; y < recipe.Height && ok; y++)
                for (var x = 0; x < recipe.Width && ok; x++)
                {
                    var want = recipe.Cells[y * recipe.Width + (mirror ? recipe.Width - 1 - x : x)];
                    var have = cells[(minY + y) * side + minX + x];
                    if (want is null) ok = have is null;
                    else if (have is null || !want.Value.Matches(have.Value)) ok = false;
                    else used.Add((minY + y) * side + minX + x);
                }

            if (ok) return true;
        }

        used.Clear();
        return false;
    }

    private static bool MatchShapeless(CraftingRecipe recipe, IReadOnlyList<GridItem?> cells, List<int> used)
    {
        var filled = new List<int>();
        for (var i = 0; i < cells.Count; i++)
            if (cells[i] is not null) filled.Add(i);
        if (filled.Count != recipe.Cells.Length) return false;

        if (!Assign(recipe, cells, filled, 0, new bool[recipe.Cells.Length])) return false;
        used.AddRange(filled);
        return true;
    }

    /// <summary>Backtracking cell→ingredient assignment (at most 9 cells), so tag/name overlaps cannot cause a false negative.</summary>
    private static bool Assign(CraftingRecipe recipe, IReadOnlyList<GridItem?> cells, List<int> filled, int at, bool[] taken)
    {
        if (at == filled.Count) return true;
        for (var k = 0; k < recipe.Cells.Length; k++)
        {
            if (taken[k] || !recipe.Cells[k]!.Value.Matches(cells[filled[at]]!.Value)) continue;
            taken[k] = true;
            if (Assign(recipe, cells, filled, at + 1, taken)) return true;
            taken[k] = false;
        }

        return false;
    }
}
