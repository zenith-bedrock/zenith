using System.IO.Compression;
using System.Text.Json;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Every grid-craftable vanilla recipe, loaded from the embedded <c>recipes.json.gz</c> (built from the BDS behaviour
/// packs by <c>tools/bedrock-data</c>, <c>bedrock-data recipes</c>). Matching is by shape (with optional mirror),
/// item name and item tag — never by aggregate counts — and a grid must contain exactly the recipe's items.
/// Net ids are assigned in data order, so they are stable for a given data file.
/// </summary>
sealed class RecipeRegistry
{
    public const string EmbeddedLogicalName = "recipes.json.gz";

    private readonly Dictionary<uint, CraftingRecipe> _byNetId = new();
    private readonly Dictionary<string, uint> _byId = new();
    private readonly ItemPalette _palette;

    private RecipeRegistry(ItemPalette palette) => _palette = palette;

    public static RecipeRegistry CreateDefault() => CreateDefault(ItemPaletteLoader.FromEmbeddedResource());

    public static RecipeRegistry CreateDefault(ItemPalette itemPalette)
    {
        Blocks.EnsureLoaded();
        using var stream = typeof(RecipeRegistry).Assembly.GetManifestResourceStream(EmbeddedLogicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedLogicalName}' not found.");
        return FromStream(stream, itemPalette);
    }

    public static RecipeRegistry FromStream(Stream gzipJson, ItemPalette itemPalette)
    {
        using var gz = new GZipStream(gzipJson, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        var reg = new RecipeRegistry(itemPalette);
        uint next = 1;
        foreach (var kind in new[] { "shaped", "shapeless" })
            foreach (var e in doc.RootElement.GetProperty(kind).EnumerateArray())
                reg.TryAdd(e, kind == "shaped", ref next);
        return reg;
    }

    private void TryAdd(JsonElement e, bool shaped, ref uint next)
    {
        var id = e.GetProperty("id").GetString()!;
        if (_byId.ContainsKey(id)) throw new InvalidDataException($"Recipe data: duplicate id '{id}'.");

        var outputs = new List<RecipeOutput>();
        foreach (var o in e.GetProperty("out").EnumerateArray())
            outputs.Add(ResolveOutput(id, o));
        if (outputs.Count == 0) throw new InvalidDataException($"Recipe data: '{id}' has no output.");

        var cells = new List<RecipeIngredient?>();
        foreach (var c in e.GetProperty("in").EnumerateArray())
        {
            if (c.ValueKind == JsonValueKind.Null) { cells.Add(null); continue; }
            var count = c.TryGetProperty("c", out var cnt) ? cnt.GetInt32() : 1;
            var name = c.TryGetProperty("n", out var n) ? n.GetString()! : null;
            if (name is not null && !IsKnownItem(name))
                throw new InvalidDataException($"Recipe data: '{id}' uses unknown item '{name}'.");
            var tag = c.TryGetProperty("t", out var t) ? t.GetString() : null;
            int? aux = c.TryGetProperty("a", out var a) ? a.GetInt32() : null;
            if (aux is not null && name is null)
                throw new InvalidDataException($"Recipe data: '{id}' puts an aux on a tag ingredient.");
            if (aux is < 0 or > StackId.MaxAux)
                throw new InvalidDataException($"Recipe data: '{id}' has aux {aux} outside 0..{StackId.MaxAux}.");
            var ingredient = new RecipeIngredient(name, tag, 1, aux);
            // One cell holds one item per craft, so a counted shapeless ingredient is N separate cells.
            for (var i = 0; i < (shaped ? 1 : count); i++) cells.Add(ingredient);
        }

        var width = shaped ? e.GetProperty("w").GetInt32() : cells.Count;
        var height = shaped ? e.GetProperty("h").GetInt32() : 1;
        if (shaped && cells.Count != width * height)
            throw new InvalidDataException($"Recipe data: '{id}' declares {width}x{height} but has {cells.Count} cells.");

        var recipe = new CraftingRecipe(
            next, id, shaped, width, height,
            shaped && e.GetProperty("sym").GetBoolean(),
            e.GetProperty("st").GetString()!,
            cells.ToArray(), outputs.ToArray());
        _byNetId[next] = recipe;
        _byId[id] = next;
        next++;
    }

    private bool IsKnownItem(string name) => Blocks.TryGetDefaultRuntimeId(name, out _) || _palette.TryGet(name, out _);

    /// <summary>
    /// Outputs are resolved by NAME against this build's palettes: a block becomes its default-state block stack,
    /// anything else an item stack. An unknown name is a data/palette mismatch and fails the load loudly.
    /// </summary>
    private RecipeOutput ResolveOutput(string recipeId, JsonElement o)
    {
        var name = o.GetProperty("n").GetString()!;
        var count = o.GetProperty("c").GetInt32();
        // An explicit aux marks an ITEM stack even when the name is also a block's (bed, banner: the item carries the
        // colour in aux and no block runtime id, as on the wire); without one a block name is its default-state block.
        if (o.TryGetProperty("a", out var auxElement))
        {
            var aux = auxElement.GetInt32();
            if (aux is < 0 or > StackId.MaxAux)
                throw new InvalidDataException($"Recipe data: '{recipeId}' has aux {aux} outside 0..{StackId.MaxAux}.");
            if (!_palette.TryGet(name, out var auxItemId))
                throw new InvalidDataException($"Recipe data: '{recipeId}' gives an aux to '{name}', which is not an item.");
            return new RecipeOutput(StackId.FromItem(auxItemId, aux), count);
        }

        if (Blocks.TryGetDefaultRuntimeId(name, out var rid))
            return new RecipeOutput(StackId.FromBlock(rid), count);
        if (_palette.TryGet(name, out var itemId))
            return new RecipeOutput(StackId.FromItem(itemId), count);
        throw new InvalidDataException($"Recipe data: '{recipeId}' makes unknown item '{name}'.");
    }

    public int Count => _byNetId.Count;

    public bool TryGetNetId(string recipeId, out uint netId) => _byId.TryGetValue(recipeId, out netId);

    public uint NetIdOf(string recipeId) =>
        _byId.TryGetValue(recipeId, out var id) ? id : throw new KeyNotFoundException($"Unknown recipe '{recipeId}'.");

    public bool TryGet(uint netId, out CraftingRecipe recipe) => _byNetId.TryGetValue(netId, out recipe!);

    /// <summary>All recipes ordered by net id — the SSOT for the CraftingData wire.</summary>
    public IReadOnlyList<CraftingRecipe> Recipes
    {
        get
        {
            var list = new List<CraftingRecipe>(_byNetId.Values);
            list.Sort((a, b) => a.NetId.CompareTo(b.NetId));
            return list;
        }
    }

    /// <summary>Name and aux of whatever sits in a grid cell (blocks and items share the namespace); null when empty.</summary>
    private GridItem? ItemOf(InventorySlot slot)
    {
        if (slot.IsEmpty) return null;
        if (slot.Id.IsBlock) return new GridItem(Blocks.TryGetName(slot.Id.Value, out var b) ? b : "", 0);
        return new GridItem(_palette.TryGetName(slot.Id.Value, out var i) ? i : "", slot.Id.Aux);
    }

    /// <summary>
    /// Whether <paramref name="grid"/> holds exactly this recipe's ingredients (shape-aware for shaped recipes);
    /// on success <paramref name="used"/> lists the grid cells one craft takes one item from.
    /// </summary>
    public bool TryMatch(CraftingRecipe recipe, ICraftGrid grid, out List<int> used)
    {
        used = [];
        var side = grid.GridSize == 4 ? 2 : 3;
        var names = new GridItem?[grid.GridSize];
        for (var i = 0; i < names.Length; i++) names[i] = ItemOf(grid.GetGrid(i));

        return recipe.Shaped
            ? MatchShaped(recipe, names, side, used)
            : MatchShapeless(recipe, names, used);
    }

    private static bool MatchShaped(CraftingRecipe recipe, GridItem?[] names, int side, List<int> used)
    {
        int minX = side, minY = side, maxX = -1, maxY = -1;
        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] is null) continue;
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
                    var have = names[(minY + y) * side + minX + x];
                    if (want is null) ok = have is null;
                    else if (have is null || !want.Value.Matches(have.Value)) ok = false;
                    else used.Add((minY + y) * side + minX + x);
                }

            if (ok) return true;
        }

        used.Clear();
        return false;
    }

    private static bool MatchShapeless(CraftingRecipe recipe, GridItem?[] names, List<int> used)
    {
        var cells = new List<int>();
        for (var i = 0; i < names.Length; i++)
            if (names[i] is not null) cells.Add(i);
        if (cells.Count != recipe.Cells.Length) return false;

        if (!Assign(recipe, names, cells, 0, new bool[recipe.Cells.Length])) return false;
        used.AddRange(cells);
        return true;
    }

    /// <summary>Backtracking cell→ingredient assignment (at most 9 cells), so tag/name overlaps cannot cause a false negative.</summary>
    private static bool Assign(CraftingRecipe recipe, GridItem?[] names, List<int> cells, int at, bool[] taken)
    {
        if (at == cells.Count) return true;
        for (var k = 0; k < recipe.Cells.Length; k++)
        {
            if (taken[k] || !recipe.Cells[k]!.Value.Matches(names[cells[at]]!.Value)) continue;
            taken[k] = true;
            if (Assign(recipe, names, cells, at + 1, taken)) return true;
            taken[k] = false;
        }

        return false;
    }

    /// <summary>
    /// Recipe-book crafting: how much of each of <paramref name="sources"/> (the open grid's cells, then the player's
    /// slots, in the order the caller wants them drained) <paramref name="times"/> crafts take. Position does not matter;
    /// every ingredient needs <c>Count × times</c> matching items. Ingredients that name an item are served before tag
    /// ingredients so a tag cell cannot eat the only exact item a later cell needs. Like <see cref="TryCraftFromGrid"/>
    /// an unaffordable or over-large request is refused, never reduced. Nothing is mutated.
    /// </summary>
    public bool TryPlanAutoCraft(
        uint netId,
        int times,
        IReadOnlyList<InventorySlot> sources,
        bool isAtCraftingTable,
        out CraftingRecipe recipe,
        out int[] takes)
    {
        takes = new int[sources.Count];
        if (!_byNetId.TryGetValue(netId, out recipe!) || recipe.Outputs.Length == 0) return false;
        if (recipe.RequiresTable && !isAtCraftingTable) return false;
        if (times <= 0) return false;

        var main = recipe.Outputs[0];
        if (main.Count <= 0 || times > PlayerInventory.MaxStack / main.Count) return false;

        var names = new GridItem?[sources.Count];
        for (var i = 0; i < names.Length; i++) names[i] = ItemOf(sources[i]);

        foreach (var ingredient in recipe.Cells.OfType<RecipeIngredient>().OrderBy(c => c.Name is null).ThenBy(c => c.Aux is null))
        {
            var need = ingredient.Count * times;
            for (var i = 0; i < names.Length && need > 0; i++)
            {
                if (names[i] is not { Name.Length: > 0 } item || !ingredient.Matches(item)) continue;
                var take = Math.Min(need, sources[i].Count - takes[i]);
                takes[i] += take;
                need -= take;
            }

            if (need > 0) { takes = new int[sources.Count]; return false; }
        }

        return true;
    }

    /// <summary>
    /// Match + consume <paramref name="times"/> crafts from <paramref name="grid"/>. A request for more crafts than the
    /// grid affords (or than one result stack can hold) is refused, not reduced: the client's own Consume list
    /// was computed for the count it asked for. The grid is restored if anything fails.
    /// </summary>
    public bool TryCraftFromGrid(ICraftGrid grid, uint netId, out InventorySlot output, int times, bool isAtCraftingTable)
    {
        output = InventorySlot.Empty;
        if (!_byNetId.TryGetValue(netId, out var recipe) || recipe.Outputs.Length == 0) return false;
        if (recipe.RequiresTable && !isAtCraftingTable) return false;
        if (times < 0) return false;
        if (times == 0) times = 1;

        var main = recipe.Outputs[0];
        if (main.Count <= 0 || times > PlayerInventory.MaxStack / main.Count) return false;
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

        output = new InventorySlot(main.Stack, main.Count * times);
        return true;
    }
}
