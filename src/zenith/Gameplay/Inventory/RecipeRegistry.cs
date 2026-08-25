using System.Collections.Generic;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// Receitas shapeless mínimas (ADR §29 / §35 / §55).
/// Exact <see cref="StackId"/> match — no SameMergeItem / facing normalize on craft.
/// NetIds estáticos reminted via CraftingDataPacket.
/// </summary>
sealed class RecipeRegistry
{
    public const uint OakLogToPlanks = 1;
    public const uint OakPlanksToChest = 2;

    // Phase XXV — smallest tool-progression chain the existing shapeless/2×2-grid recipe model can
    // express (ADR §108's sibling decision — see the Phase XXV findings doc): wood and stone tiers
    // use directly-mined materials; diamond skips straight from raw ore (vanilla doesn't smelt
    // diamond either, via BlockLoot's ore→item mapping). Iron/gold are deliberately NOT wired to a
    // tool recipe yet — their vanilla ingot form requires smelting, and no furnace exists in Zenith;
    // recipes here only use materials a player can actually obtain today, not simulate crafting
    // options currently sitting on the far side of an unbuilt feature.
    public const uint PlanksToStick = 3;
    public const uint WoodenPickaxe = 4;
    public const uint WoodenAxe = 5;
    public const uint WoodenShovel = 6;
    public const uint StonePickaxe = 7;
    public const uint StoneAxe = 8;
    public const uint StoneShovel = 9;
    public const uint DiamondPickaxe = 10;
    public const uint DiamondAxe = 11;
    public const uint DiamondShovel = 12;

    // ADR §138 — building/decoration blocks, same "materials obtainable today" bar as the tool
    // chain above: coal is mined directly (BlockLoot), no furnace/smelting needed for any of these.
    public const uint CoalAndStickToTorch = 13;
    public const uint PlanksAndStickToFence = 14;
    public const uint StoneToStoneBricks = 15;
    public const uint CobblestoneToWall = 16;

    /// <summary>ADR §139 — the crafting table itself; obviously does not require one to make.</summary>
    public const uint PlanksToCraftingTable = 17;

    private readonly Dictionary<uint, Recipe> _byNetId = new();
    private bool _frozen;

    readonly record struct Recipe(uint NetId, (StackId Id, int Count)[] Inputs, StackId Output, int OutCount, bool RequiresTable);

    /// <summary>Wire/DTO-facing recipe rows — no packets dependency.</summary>
    public readonly record struct RecipeSnapshot(
        uint NetId,
        (StackId Id, int Count)[] Inputs,
        StackId Output,
        int OutCount,
        bool RequiresTable);

    public static RecipeRegistry CreateDefault() =>
        CreateDefault(ItemPaletteLoader.FromEmbeddedResource());

    public static RecipeRegistry CreateDefault(ItemPalette itemPalette)
    {
        Blocks.EnsureLoaded();
        var stick = StackId.FromItem(itemPalette.Require("minecraft:stick"));
        var diamond = StackId.FromItem(itemPalette.Require("minecraft:diamond"));
        var coal = StackId.FromItem(itemPalette.Require("minecraft:coal"));
        var planks = StackId.FromBlock(Blocks.OakPlanks);
        var stone = StackId.FromBlock(Blocks.Stone);
        var cobblestone = StackId.FromBlock(Blocks.Cobblestone);

        // ADR §139 — RequiresTable reflects each recipe's REAL vanilla shape bounding box, not a
        // guess: anything whose minimal shape needs more than 2 rows or 2 columns cannot physically
        // fit the personal 2×2 grid, regardless of Zenith's own matcher being shapeless (aggregate
        // count, not arrangement). Verified per recipe below.
        var reg = new RecipeRegistry();
        // 1 log -> 4 planks: vanilla's "any of the 4 cells" special case. No table.
        reg.Register(OakLogToPlanks, [(StackId.FromBlock(Blocks.OakLog), 1)], planks, 4, requiresTable: false);
        // Chest: 8 planks in a ring around an empty center — 3x3 bounding box. Table required (the
        // gap this whole ADR closes).
        reg.Register(OakPlanksToChest, [(planks, 8)], StackId.FromBlock(Blocks.Chest), 1, requiresTable: true);

        // Stick: 2 planks stacked 1 wide x 2 tall — fits 2x2. No table.
        reg.Register(PlanksToStick, [(planks, 2)], stick, 4, requiresTable: false);

        // Pickaxe: 3 materials across the top row + 2 sticks down the middle column below them —
        // 3 wide x 3 tall. Axe: 2 wide x 3 tall. Shovel: 1 wide x 3 tall. All three exceed 2 rows —
        // all require a table, same as real vanilla.
        reg.Register(WoodenPickaxe, [(planks, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:wooden_pickaxe"), 1, requiresTable: true);
        reg.Register(WoodenAxe, [(planks, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:wooden_axe"), 1, requiresTable: true);
        reg.Register(WoodenShovel, [(planks, 1), (stick, 2)], ToolStack(itemPalette, "minecraft:wooden_shovel"), 1, requiresTable: true);

        reg.Register(StonePickaxe, [(stone, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:stone_pickaxe"), 1, requiresTable: true);
        reg.Register(StoneAxe, [(stone, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:stone_axe"), 1, requiresTable: true);
        reg.Register(StoneShovel, [(stone, 1), (stick, 2)], ToolStack(itemPalette, "minecraft:stone_shovel"), 1, requiresTable: true);

        reg.Register(DiamondPickaxe, [(diamond, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:diamond_pickaxe"), 1, requiresTable: true);
        reg.Register(DiamondAxe, [(diamond, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:diamond_axe"), 1, requiresTable: true);
        reg.Register(DiamondShovel, [(diamond, 1), (stick, 2)], ToolStack(itemPalette, "minecraft:diamond_shovel"), 1, requiresTable: true);

        // Torch: 1 coal on top of 1 stick — 1 wide x 2 tall. Fits 2x2. No table.
        reg.Register(CoalAndStickToTorch, [(coal, 1), (stick, 1)], StackId.FromBlock(Blocks.Torch), 4, requiresTable: false);
        // Fence: two columns of [plank, stick] side by side — 3 wide x 2 tall. Table required.
        reg.Register(PlanksAndStickToFence, [(planks, 4), (stick, 2)], StackId.FromBlock(Blocks.OakFence), 3, requiresTable: true);
        // Stone bricks: a plain 2x2 block of stone. Exactly fits. No table.
        reg.Register(StoneToStoneBricks, [(stone, 4)], StackId.FromBlock(Blocks.StoneBricks), 4, requiresTable: false);
        // Wall: 3 wide x 2 tall row of cobblestone. Table required.
        reg.Register(CobblestoneToWall, [(cobblestone, 6)], StackId.FromBlock(Blocks.CobblestoneWall), 6, requiresTable: true);

        // The table itself: a plain 2x2 block of planks. Fits the very grid it makes obsolete —
        // matches real vanilla, which lets you make your first table without one.
        reg.Register(PlanksToCraftingTable, [(planks, 4)], StackId.FromBlock(Blocks.CraftingTable), 1, requiresTable: false);

        return reg;
    }

    private static StackId ToolStack(ItemPalette itemPalette, string name) =>
        StackId.FromItem(itemPalette.Require(name));

    /// <summary>
    /// Boot-time-only. Takes plain composition data rather than the private <see cref="Recipe"/>
    /// type — composition data a caller provides and this registry's internal canonical
    /// representation stay decoupled, so this signature would still make sense if this method is
    /// ever promoted beyond <c>private</c> for a real second caller (none exists yet). Kept
    /// <c>private</c>: <see cref="CreateDefault(ItemPalette)"/> is still the only caller.
    /// </summary>
    private void Register(uint netId, (StackId Id, int Count)[] inputs, StackId output, int outCount, bool requiresTable = false)
    {
        if (_frozen)
            throw new InvalidOperationException("RecipeRegistry is frozen; register before Freeze().");
        if (_byNetId.ContainsKey(netId))
            throw new InvalidOperationException($"Duplicate recipe net id {netId}.");
        _byNetId[netId] = new Recipe(netId, inputs.ToArray(), output, outCount, requiresTable);
    }

    /// <summary>
    /// Marks composition complete — no further <see cref="Register"/> calls are accepted afterward.
    /// Called once by the composition root right after <see cref="CreateDefault(ItemPalette)"/>,
    /// before GameLoop starts ticking. Compose → freeze → gameplay reads only.
    /// </summary>
    internal void Freeze() => _frozen = true;

    /// <summary>Stable ordered snapshot for CraftingData wire (ADR §35) — SSOT for net ids.</summary>
    public IReadOnlyList<RecipeSnapshot> SnapshotRecipes()
    {
        var list = new List<RecipeSnapshot>(_byNetId.Count);
        foreach (var recipe in _byNetId.Values)
        {
            list.Add(new RecipeSnapshot(
                recipe.NetId,
                recipe.Inputs.ToArray(), // defensive copy — Freeze() must mean existing entries can't be mutated via an escaped array either
                recipe.Output,
                recipe.OutCount,
                recipe.RequiresTable));
        }

        list.Sort((a, b) => a.NetId.CompareTo(b.NetId));
        return list;
    }

    public bool TryGet(
        uint recipeNetId,
        out StackId output,
        out int outCount,
        out (StackId Id, int Count)[] inputs,
        out bool requiresTable)
    {
        if (!_byNetId.TryGetValue(recipeNetId, out var recipe))
        {
            output = default;
            outCount = 0;
            inputs = [];
            requiresTable = false;
            return false;
        }

        output = recipe.Output;
        outCount = recipe.OutCount;
        inputs = recipe.Inputs.ToArray(); // defensive copy — same reason as SnapshotRecipes()
        requiresTable = recipe.RequiresTable;
        return true;
    }

    /// <summary>Shapeless: aggregated counts per exact <see cref="StackId"/> must match one recipe.</summary>
    public bool TryMatch(IReadOnlyList<(StackId Id, int Count)> inputs, out InventorySlot output)
    {
        var agg = Aggregate(inputs);
        foreach (var recipe in _byNetId.Values)
        {
            if (!ExactMatch(agg, recipe.Inputs)) continue;
            output = new InventorySlot(recipe.Output, recipe.OutCount);
            return true;
        }

        output = InventorySlot.Empty;
        return false;
    }

    /// <summary>Consome inputs do inventário e adiciona output (all-or-nothing via snapshot).</summary>
    public bool TryCraft(PlayerInventory inventory, uint recipeNetId)
    {
        if (!TryGet(recipeNetId, out var outId, out var outCount, out var needed, out _))
            return false;

        var snapshot = inventory.CaptureSnapshot();
        foreach (var (id, count) in needed)
        {
            if (!inventory.TryConsume(id, count))
            {
                inventory.RestoreSnapshot(snapshot);
                return false;
            }
        }

        if (!inventory.TryAdd(outId, outCount))
        {
            inventory.RestoreSnapshot(snapshot);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Match + consume craft-grid slots × <paramref name="times"/>; output for CreatedOutput.
    /// Times clamped by grid affordability and single-slot MaxStack (H0).
    /// Item stacks in the grid do not match block recipes (exact StackId). <paramref name="craftUi"/>
    /// is either the player's personal <see cref="PlayerCraftUi"/> (2×2) or the crafting table's
    /// <see cref="PlayerTableCraftUi"/> (3×3) — the caller picks which, based on which is actually
    /// open; this method only cares about <see cref="ICraftGrid"/>'s shape (ADR §139).
    /// <paramref name="isAtCraftingTable"/> gates recipes whose real vanilla shape does not fit a
    /// 2×2 grid — refused here even if the aggregate-count match would otherwise succeed, since
    /// Zenith's shapeless matcher has no other way to know the ingredients don't actually arrange
    /// into a valid shape that small.
    /// </summary>
    public bool TryCraftFromGrid(ICraftGrid craftUi, uint recipeNetId, out InventorySlot output, int times, bool isAtCraftingTable)
    {
        output = InventorySlot.Empty;
        if (!TryGet(recipeNetId, out var outId, out var outCount, out var needed, out var requiresTable))
            return false;

        if (requiresTable && !isAtCraftingTable)
            return false;

        if (outCount <= 0 || times < 0)
            return false;

        if (times == 0)
            times = 1;

        var gridSize = craftUi.GridSize;
        var inputs = new List<(StackId Id, int Count)>(gridSize);
        for (var i = 0; i < gridSize; i++)
        {
            var slot = craftUi.GetGrid(i);
            if (!slot.IsEmpty)
                inputs.Add((slot.Id, slot.Count));
        }

        var agg = Aggregate(inputs);
        var maxByStack = PlayerInventory.MaxStack / outCount;
        if (maxByStack < 1)
            return false;

        var maxAffordable = maxByStack;
        foreach (var (id, count) in needed)
        {
            if (!agg.TryGetValue(id, out var have) || have < count)
                return false;
            maxAffordable = Math.Min(maxAffordable, have / count);
        }

        if (maxAffordable < 1)
            return false;

        times = Math.Clamp(times, 1, maxAffordable);

        var snap = craftUi.CaptureSnapshot();
        foreach (var (id, count) in needed)
        {
            var remaining = count * times;
            for (var i = 0; i < gridSize && remaining > 0; i++)
            {
                var slot = craftUi.GetGrid(i);
                if (slot.IsEmpty || slot.Id != id) continue;
                var take = Math.Min(remaining, slot.Count);
                remaining -= take;
                var left = slot.Count - take;
                if (!craftUi.TrySetGrid(i, left == 0 ? InventorySlot.Empty : slot with { Count = left }))
                {
                    craftUi.RestoreSnapshot(snap);
                    return false;
                }
            }

            if (remaining > 0)
            {
                craftUi.RestoreSnapshot(snap);
                return false;
            }
        }

        output = new InventorySlot(outId, outCount * times);
        return true;
    }

    private static Dictionary<StackId, int> Aggregate(IReadOnlyList<(StackId Id, int Count)> inputs)
    {
        var dict = new Dictionary<StackId, int>();
        foreach (var (id, count) in inputs)
        {
            if (count <= 0 || id.IsEmpty) continue;
            dict[id] = dict.GetValueOrDefault(id) + count;
        }

        return dict;
    }

    private static bool ExactMatch(Dictionary<StackId, int> agg, (StackId Id, int Count)[] needed)
    {
        if (agg.Count != needed.Length) return false;
        foreach (var (id, count) in needed)
        {
            if (!agg.TryGetValue(id, out var have) || have != count)
                return false;
        }

        return true;
    }
}
