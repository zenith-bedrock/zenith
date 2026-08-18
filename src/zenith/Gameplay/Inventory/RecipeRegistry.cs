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

    private readonly Dictionary<uint, Recipe> _byNetId = new();
    private bool _frozen;

    readonly record struct Recipe(uint NetId, (StackId Id, int Count)[] Inputs, StackId Output, int OutCount);

    /// <summary>Wire/DTO-facing recipe rows — no packets dependency.</summary>
    public readonly record struct RecipeSnapshot(
        uint NetId,
        (StackId Id, int Count)[] Inputs,
        StackId Output,
        int OutCount);

    public static RecipeRegistry CreateDefault() =>
        CreateDefault(ItemPaletteLoader.FromEmbeddedResource());

    public static RecipeRegistry CreateDefault(ItemPalette itemPalette)
    {
        Blocks.EnsureLoaded();
        var stick = StackId.FromItem(itemPalette.Require("minecraft:stick"));
        var diamond = StackId.FromItem(itemPalette.Require("minecraft:diamond"));
        var planks = StackId.FromBlock(Blocks.OakPlanks);
        var stone = StackId.FromBlock(Blocks.Stone);

        var reg = new RecipeRegistry();
        reg.Register(OakLogToPlanks, [(StackId.FromBlock(Blocks.OakLog), 1)], planks, 4);
        reg.Register(OakPlanksToChest, [(planks, 8)], StackId.FromBlock(Blocks.Chest), 1);

        reg.Register(PlanksToStick, [(planks, 2)], stick, 4);

        reg.Register(WoodenPickaxe, [(planks, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:wooden_pickaxe"), 1);
        reg.Register(WoodenAxe, [(planks, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:wooden_axe"), 1);
        reg.Register(WoodenShovel, [(planks, 1), (stick, 2)], ToolStack(itemPalette, "minecraft:wooden_shovel"), 1);

        reg.Register(StonePickaxe, [(stone, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:stone_pickaxe"), 1);
        reg.Register(StoneAxe, [(stone, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:stone_axe"), 1);
        reg.Register(StoneShovel, [(stone, 1), (stick, 2)], ToolStack(itemPalette, "minecraft:stone_shovel"), 1);

        reg.Register(DiamondPickaxe, [(diamond, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:diamond_pickaxe"), 1);
        reg.Register(DiamondAxe, [(diamond, 3), (stick, 2)], ToolStack(itemPalette, "minecraft:diamond_axe"), 1);
        reg.Register(DiamondShovel, [(diamond, 1), (stick, 2)], ToolStack(itemPalette, "minecraft:diamond_shovel"), 1);

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
    private void Register(uint netId, (StackId Id, int Count)[] inputs, StackId output, int outCount)
    {
        if (_frozen)
            throw new InvalidOperationException("RecipeRegistry is frozen; register before Freeze().");
        if (_byNetId.ContainsKey(netId))
            throw new InvalidOperationException($"Duplicate recipe net id {netId}.");
        _byNetId[netId] = new Recipe(netId, inputs.ToArray(), output, outCount);
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
        inputs = recipe.Inputs.ToArray(); // defensive copy — same reason as SnapshotRecipes()
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
