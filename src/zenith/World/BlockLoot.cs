using System.Collections.Frozen;
namespace Zenith.World;

/// <summary>
/// Phase XXV — ore blocks whose vanilla drop is a distinct item, not the block itself, with no
/// smelting required (coal/diamond/redstone/lapis all drop their raw gem/dust directly in vanilla).
/// Iron/gold/copper deliberately excluded: their vanilla drop requires smelting (a furnace), which
/// does not exist in Zenith yet — breaking those ores still yields the ore block itself, which
/// remains the correct smelting *input* for whenever that lands, not a silent wrong answer today.
/// A data lookup, not a loot-table framework — extend only when a specific ore's real drop is wrong.
/// </summary>
static class BlockLoot
{
    private static readonly Lock LoadGate = new();

    /// <summary>Immutable table published by one reference write (see <c>Tools</c>): a reload never exposes a half-filled map.</summary>
    private static volatile FrozenDictionary<int, StackId>? _oreDrops;

    public static void EnsureLoaded() => _ = OreDrops;

    private static FrozenDictionary<int, StackId> OreDrops
    {
        get
        {
            if (_oreDrops is { } table) return table;
            lock (LoadGate)
                return _oreDrops ??= Build(ItemPaletteLoader.FromEmbeddedResource());
        }
    }

    public static void Load(ItemPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var table = Build(palette);
        lock (LoadGate)
            _oreDrops = table;
    }

    private static FrozenDictionary<int, StackId> Build(ItemPalette palette)
    {
        var drops = new Dictionary<int, StackId>();
        RegisterOreDrop(drops, Blocks.CoalOre, palette.Require("minecraft:coal"));
        RegisterOreDrop(drops, Blocks.DeepslateCoalOre, palette.Require("minecraft:coal"));
        RegisterOreDrop(drops, Blocks.DiamondOre, palette.Require("minecraft:diamond"));
        RegisterOreDrop(drops, Blocks.DeepslateDiamondOre, palette.Require("minecraft:diamond"));
        RegisterOreDrop(drops, Blocks.RedstoneOre, palette.Require("minecraft:redstone"));
        RegisterOreDrop(drops, Blocks.DeepslateRedstoneOre, palette.Require("minecraft:redstone"));
        RegisterOreDrop(drops, Blocks.LapisOre, palette.Require("minecraft:lapis_lazuli"));
        RegisterOreDrop(drops, Blocks.DeepslateLapisOre, palette.Require("minecraft:lapis_lazuli"));
        return drops.ToFrozenDictionary();
    }

    private static void RegisterOreDrop(Dictionary<int, StackId> drops, int blockRuntimeId, int itemNetworkId) =>
        drops[blockRuntimeId] = StackId.FromItem(itemNetworkId);

    /// <summary>Drop for breaking <paramref name="blockRuntimeId"/> — the mapped item for the
    /// no-smelt ores above, otherwise the block itself (existing default behavior).</summary>
    public static StackId DropFor(int blockRuntimeId)
    {
        return OreDrops.TryGetValue(blockRuntimeId, out var item)
            ? item
            : StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(blockRuntimeId));
    }
}
