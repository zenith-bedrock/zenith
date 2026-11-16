using System.Collections.Frozen;
namespace Zenith.World;

/// <summary>
/// What breaking a block yields. The exceptions to "a broken block drops itself" (stone → cobblestone, glass drops
/// nothing, ores drop their gem, a door drops its item, a double slab drops two slabs) are data on the block's
/// <see cref="BlockDefinition.Drop"/>; this class resolves those names against the item palette once.
/// </summary>
static class BlockLoot
{
    private static readonly Lock LoadGate = new();

    /// <summary>Immutable table published by one reference write (see <c>Tools</c>): a reload never exposes a half-filled map.</summary>
    private static volatile FrozenDictionary<string, BlockDrop>? _drops;

    public static void EnsureLoaded() => _ = Drops;

    private static FrozenDictionary<string, BlockDrop> Drops
    {
        get
        {
            if (_drops is { } table) return table;
            lock (LoadGate)
                return _drops ??= Build(ItemPaletteLoader.FromEmbeddedResource());
        }
    }

    public static void Load(ItemPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var table = Build(palette);
        lock (LoadGate)
            _drops = table;
    }

    private static FrozenDictionary<string, BlockDrop> Build(ItemPalette items)
    {
        var drops = new Dictionary<string, BlockDrop>(StringComparer.Ordinal);
        foreach (var family in Blocks.Catalog.States.Families)
        {
            if (family.Definition?.Drop is not { } spec) continue;
            drops[family.Name] = Resolve(family.Name, spec, items);
        }
        return drops.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static BlockDrop Resolve(string block, DropSpec spec, ItemPalette items)
    {
        if (spec.DropsNothing) return default;
        if (spec.Item is { } item)
            return items.TryGet(item, out var itemId)
                ? new BlockDrop(StackId.FromItem(itemId), spec.Count)
                : throw new InvalidOperationException($"{block} drops unknown item '{item}'.");

        var target = spec.Block ?? throw new InvalidOperationException($"{block} drop names neither a block nor an item.");
        return Blocks.TryGetDefaultRuntimeId(target, out var blockId)
            ? new BlockDrop(StackId.FromBlock(blockId), spec.Count)
            : throw new InvalidOperationException($"{block} drops unknown block '{target}'.");
    }

    /// <summary>
    /// What breaking <paramref name="blockRuntimeId"/> yields: its data-defined drop, otherwise the block itself in its
    /// default state. <see cref="BlockDrop.Count"/> 0 means "drops nothing".
    /// </summary>
    public static BlockDrop DropOf(int blockRuntimeId)
    {
        if (Blocks.Catalog.States.FamilyOf(blockRuntimeId) is { } family && Drops.TryGetValue(family.Name, out var drop))
            return drop;
        return new BlockDrop(StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(blockRuntimeId)), 1);
    }

    /// <summary>Single-stack form of <see cref="DropOf"/> kept for callers that only need the item identity.</summary>
    public static StackId DropFor(int blockRuntimeId) => DropOf(blockRuntimeId).Id;
}

/// <summary>A broken block's yield; <see cref="Count"/> 0 means nothing drops.</summary>
readonly record struct BlockDrop(StackId Id, int Count);
