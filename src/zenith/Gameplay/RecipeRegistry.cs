using System.Collections.Generic;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay;

/// <summary>
/// Receitas shapeless mínimas (ADR §29). NetIds estáticos Zenith — craft wire completo
/// exige <c>CraftingDataPacket</c> no follow-up.
/// </summary>
sealed class RecipeRegistry
{
    public const uint OakLogToPlanks = 1;
    public const uint OakPlanksToChest = 2;

    private readonly Dictionary<uint, Recipe> _byNetId = new();

    readonly record struct Recipe(uint NetId, (int RuntimeId, int Count)[] Inputs, int OutRuntimeId, int OutCount);

    public static RecipeRegistry CreateDefault()
    {
        Blocks.EnsureLoaded();
        var reg = new RecipeRegistry();
        reg.Register(new Recipe(
            OakLogToPlanks,
            [(Blocks.OakLog, 1)],
            Blocks.OakPlanks,
            4));
        reg.Register(new Recipe(
            OakPlanksToChest,
            [(Blocks.OakPlanks, 8)],
            Blocks.Chest,
            1));
        return reg;
    }

    private void Register(in Recipe recipe) => _byNetId[recipe.NetId] = recipe;

    public bool TryGet(uint recipeNetId, out int outRuntimeId, out int outCount, out (int RuntimeId, int Count)[] inputs)
    {
        if (!_byNetId.TryGetValue(recipeNetId, out var recipe))
        {
            outRuntimeId = Blocks.Air;
            outCount = 0;
            inputs = [];
            return false;
        }

        outRuntimeId = recipe.OutRuntimeId;
        outCount = recipe.OutCount;
        inputs = recipe.Inputs;
        return true;
    }

    /// <summary>Shapeless: contagens agregadas por runtimeId devem casar exactamente uma receita.</summary>
    public bool TryMatch(IReadOnlyList<(int RuntimeId, int Count)> inputs, out InventorySlot output)
    {
        var agg = Aggregate(inputs);
        foreach (var recipe in _byNetId.Values)
        {
            if (!ExactMatch(agg, recipe.Inputs)) continue;
            output = new InventorySlot(recipe.OutRuntimeId, recipe.OutCount);
            return true;
        }

        output = InventorySlot.Empty;
        return false;
    }

    /// <summary>Consome inputs do inventário e adiciona output (all-or-nothing via snapshot).</summary>
    public bool TryCraft(PlayerInventory inventory, uint recipeNetId)
    {
        if (!TryGet(recipeNetId, out var outRid, out var outCount, out var needed))
            return false;

        var snapshot = inventory.CaptureSnapshot();
        foreach (var (rid, count) in needed)
        {
            if (!inventory.TryConsume(rid, count))
            {
                inventory.RestoreSnapshot(snapshot);
                return false;
            }
        }

        if (!inventory.TryAdd(outRid, outCount))
        {
            inventory.RestoreSnapshot(snapshot);
            return false;
        }

        return true;
    }

    private static Dictionary<int, int> Aggregate(IReadOnlyList<(int RuntimeId, int Count)> inputs)
    {
        var dict = new Dictionary<int, int>();
        foreach (var (rid, count) in inputs)
        {
            if (count <= 0 || rid == Blocks.Air) continue;
            dict[rid] = dict.GetValueOrDefault(rid) + count;
        }

        return dict;
    }

    private static bool ExactMatch(Dictionary<int, int> agg, (int RuntimeId, int Count)[] needed)
    {
        if (agg.Count != needed.Length) return false;
        foreach (var (rid, count) in needed)
        {
            if (!agg.TryGetValue(rid, out var have) || have != count)
                return false;
        }

        return true;
    }
}
