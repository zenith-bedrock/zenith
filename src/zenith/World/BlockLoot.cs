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
    private static readonly object LoadGate = new();
    private static readonly Dictionary<int, StackId> OreDropByBlockRuntimeId = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (LoadGate)
        {
            if (_loaded) return;
            LoadUnlocked(ItemPaletteLoader.FromEmbeddedResource());
        }
    }

    public static void Load(ItemPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        lock (LoadGate)
            LoadUnlocked(palette);
    }

    internal static void ResetForTests()
    {
        lock (LoadGate)
        {
            _loaded = false;
            OreDropByBlockRuntimeId.Clear();
        }
    }

    private static void LoadUnlocked(ItemPalette palette)
    {
        OreDropByBlockRuntimeId.Clear();
        RegisterOreDrop(Blocks.CoalOre, palette.Require("minecraft:coal"));
        RegisterOreDrop(Blocks.DeepslateCoalOre, palette.Require("minecraft:coal"));
        RegisterOreDrop(Blocks.DiamondOre, palette.Require("minecraft:diamond"));
        RegisterOreDrop(Blocks.DeepslateDiamondOre, palette.Require("minecraft:diamond"));
        RegisterOreDrop(Blocks.RedstoneOre, palette.Require("minecraft:redstone"));
        RegisterOreDrop(Blocks.DeepslateRedstoneOre, palette.Require("minecraft:redstone"));
        RegisterOreDrop(Blocks.LapisOre, palette.Require("minecraft:lapis_lazuli"));
        RegisterOreDrop(Blocks.DeepslateLapisOre, palette.Require("minecraft:lapis_lazuli"));
        _loaded = true;
    }

    private static void RegisterOreDrop(int blockRuntimeId, int itemNetworkId) =>
        OreDropByBlockRuntimeId[blockRuntimeId] = StackId.FromItem(itemNetworkId);

    /// <summary>Drop for breaking <paramref name="blockRuntimeId"/> — the mapped item for the
    /// no-smelt ores above, otherwise the block itself (existing default behavior).</summary>
    public static StackId DropFor(int blockRuntimeId)
    {
        EnsureLoaded();
        return OreDropByBlockRuntimeId.TryGetValue(blockRuntimeId, out var item)
            ? item
            : StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(blockRuntimeId));
    }
}
