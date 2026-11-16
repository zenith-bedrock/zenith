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

    /// <summary>
    /// What breaking <paramref name="blockRuntimeId"/> yields: the mapped item for the no-smelt ores, a redirected or
    /// absent drop for the blocks vanilla treats specially (stone → cobblestone, glass drops nothing…), otherwise the
    /// block itself in its default state. <see cref="BlockDrop.Count"/> 0 means "drops nothing".
    /// </summary>
    public static BlockDrop DropOf(int blockRuntimeId)
    {
        if (OreDrops.TryGetValue(blockRuntimeId, out var item))
            return new BlockDrop(item, 1);

        if (Blocks.Catalog.KindOf(blockRuntimeId) == BlockKind.Door)
        {
            // A door is one item whichever half is broken (the other half is removed without a drop); the item is not the block form.
            if (Blocks.TryGetName(blockRuntimeId, out var doorName) && DoorItems.Value.TryGet(doorName, out var doorItem))
                return new BlockDrop(StackId.FromItem(doorItem), 1);
        }

        if (Blocks.TryGetName(blockRuntimeId, out var name))
        {
            if (BlockDropRules.TryRedirect(name, out var redirect, out var count))
            {
                if (count == 0) return default;
                if (Blocks.TryGetDefaultRuntimeId(redirect, out var redirectId))
                    return new BlockDrop(StackId.FromBlock(redirectId), count);
            }
        }

        return new BlockDrop(StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(blockRuntimeId)), 1);
    }

    private static readonly Lazy<ItemPalette> DoorItems = new(() => ItemPaletteLoader.FromEmbeddedResource());

    /// <summary>Single-stack form of <see cref="DropOf"/> kept for callers that only need the item identity.</summary>
    public static StackId DropFor(int blockRuntimeId) => DropOf(blockRuntimeId).Id;
}

/// <summary>A broken block's yield; <see cref="Count"/> 0 means nothing drops.</summary>
readonly record struct BlockDrop(StackId Id, int Count);

/// <summary>Name-driven exceptions to "a broken block drops itself", matched on flattened Bedrock names.</summary>
static class BlockDropRules
{
    public static bool TryRedirect(string name, out string redirect, out int count)
    {
        redirect = "";
        count = 1;
        switch (name)
        {
            case "minecraft:stone": redirect = "minecraft:cobblestone"; return true;
            case "minecraft:deepslate": redirect = "minecraft:cobbled_deepslate"; return true;
            case "minecraft:grass_block":
            case "minecraft:podzol":
            case "minecraft:mycelium":
            case "minecraft:farmland":
            case "minecraft:grass_path":
            case "minecraft:dirt_path":
                redirect = "minecraft:dirt";
                return true;
            case "minecraft:short_grass":
            case "minecraft:fern":
            case "minecraft:dead_bush":
                count = 0;
                return true;
        }

        if (name.Contains("glass", StringComparison.Ordinal) && !name.Contains("glazed", StringComparison.Ordinal) ||
            name.Contains("leaves", StringComparison.Ordinal) ||
            name == "minecraft:ice")
        {
            count = 0;
            return true;
        }

        if (name.EndsWith("_double_slab", StringComparison.Ordinal))
        {
            redirect = name[..^"_double_slab".Length] + "_slab";
            count = 2;
            return true;
        }

        return false;
    }
}
