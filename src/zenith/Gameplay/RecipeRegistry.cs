using System.Collections.Generic;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay;

/// <summary>
/// Receitas shapeless mínimas (ADR §29 / §35 / §55).
/// Exact <see cref="StackId"/> match — no SameMergeItem / facing normalize on craft.
/// NetIds estáticos reminted via CraftingDataPacket.
/// </summary>
sealed class RecipeRegistry
{
    public const uint OakLogToPlanks = 1;
    public const uint OakPlanksToChest = 2;

    private readonly Dictionary<uint, Recipe> _byNetId = new();

    readonly record struct Recipe(uint NetId, (StackId Id, int Count)[] Inputs, StackId Output, int OutCount);

    /// <summary>Wire/DTO-facing recipe rows — no packets dependency.</summary>
    public readonly record struct RecipeSnapshot(
        uint NetId,
        (StackId Id, int Count)[] Inputs,
        StackId Output,
        int OutCount);

    public static RecipeRegistry CreateDefault()
    {
        Blocks.EnsureLoaded();
        var reg = new RecipeRegistry();
        reg.Register(new Recipe(
            OakLogToPlanks,
            [(StackId.FromBlock(Blocks.OakLog), 1)],
            StackId.FromBlock(Blocks.OakPlanks),
            4));
        reg.Register(new Recipe(
            OakPlanksToChest,
            [(StackId.FromBlock(Blocks.OakPlanks), 8)],
            StackId.FromBlock(Blocks.Chest),
            1));
        return reg;
    }

    private void Register(in Recipe recipe) => _byNetId[recipe.NetId] = recipe;

    /// <summary>Stable ordered snapshot for CraftingData wire (ADR §35) — SSOT for net ids.</summary>
    public IReadOnlyList<RecipeSnapshot> SnapshotRecipes()
    {
        var list = new List<RecipeSnapshot>(_byNetId.Count);
        foreach (var recipe in _byNetId.Values)
        {
            list.Add(new RecipeSnapshot(
                recipe.NetId,
                recipe.Inputs,
                recipe.Output,
                recipe.OutCount));
        }

        list.Sort((a, b) => a.NetId.CompareTo(b.NetId));
        return list;
    }

    public bool TryGet(
        uint recipeNetId,
        out StackId output,
        out int outCount,
        out (StackId Id, int Count)[] inputs)
    {
        if (!_byNetId.TryGetValue(recipeNetId, out var recipe))
        {
            output = default;
            outCount = 0;
            inputs = [];
            return false;
        }

        output = recipe.Output;
        outCount = recipe.OutCount;
        inputs = recipe.Inputs;
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
        if (!TryGet(recipeNetId, out var outId, out var outCount, out var needed))
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
    /// Match + consume 2×2 grid slots × <paramref name="times"/>; output for CreatedOutput.
    /// Times clamped by grid affordability and single-slot MaxStack (H0).
    /// Item stacks in the grid do not match block recipes (exact StackId).
    /// </summary>
    public bool TryCraftFromGrid(PlayerCraftUi craftUi, uint recipeNetId, out InventorySlot output, int times = 1)
    {
        output = InventorySlot.Empty;
        if (!TryGet(recipeNetId, out var outId, out var outCount, out var needed))
            return false;

        if (outCount <= 0 || times < 0)
            return false;

        if (times == 0)
            times = 1;

        var inputs = new List<(StackId Id, int Count)>(PlayerCraftUi.GridSize);
        for (var i = 0; i < PlayerCraftUi.GridSize; i++)
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
            for (var i = 0; i < PlayerCraftUi.GridSize && remaining > 0; i++)
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
