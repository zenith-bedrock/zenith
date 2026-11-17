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
    private static volatile FrozenDictionary<string, ResolvedDrop>? _drops;

    public static void EnsureLoaded() => _ = Drops;

    private static FrozenDictionary<string, ResolvedDrop> Drops
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

    private static FrozenDictionary<string, ResolvedDrop> Build(ItemPalette items)
    {
        var drops = new Dictionary<string, ResolvedDrop>(StringComparer.Ordinal);
        foreach (var family in Blocks.Catalog.States.Families)
        {
            if (family.Definition?.Drop is not { } spec) continue;
            drops[family.Name] = ResolveAll(family.Name, spec, items);
        }
        return drops.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static ResolvedDrop ResolveAll(string block, DropSpec spec, ItemPalette items) => new(
        Resolve(block, spec, items),
        Math.Max(spec.Count, spec.CountMax),
        spec.Instead is { } instead ? [.. instead.Select(entry => Chanced(block, entry, items))] : [],
        spec.Bonus is { } bonus ? [.. bonus.Select(entry => Chanced(block, entry, items))] : []);

    private static ChancedDrop Chanced(string block, DropSpec entry, ItemPalette items) =>
        new(Resolve(block, entry, items), Math.Max(entry.Count, entry.CountMax), entry.Chance);

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
            return drop.Main;
        return new BlockDrop(StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(blockRuntimeId)), 1);
    }

    /// <summary>
    /// What one break of <paramref name="blockRuntimeId"/> actually yields, with the dice rolled: the main drop (its count drawn from
    /// its range) unless an "instead" entry came up, plus every bonus entry that came up. Appends to <paramref name="into"/>.
    /// </summary>
    public static void Roll(int blockRuntimeId, Random random, List<BlockDrop> into)
    {
        if (Blocks.Catalog.States.FamilyOf(blockRuntimeId) is not { } family || !Drops.TryGetValue(family.Name, out var drop))
        {
            into.Add(new BlockDrop(StackId.FromBlock(Blocks.NormalizeMergeRuntimeId(blockRuntimeId)), 1));
            return;
        }

        var replaced = false;
        foreach (var entry in drop.Instead)
            if (entry.Rolls(random))
            {
                into.Add(entry.Draw(random));
                replaced = true;
                break;
            }

        if (!replaced && drop.Main.Count > 0)
            into.Add(new BlockDrop(drop.Main.Id, drop.MainMax > drop.Main.Count ? random.Next(drop.Main.Count, drop.MainMax + 1) : drop.Main.Count));

        foreach (var entry in drop.Bonus)
            if (entry.Rolls(random))
                into.Add(entry.Draw(random));
    }

    /// <summary>Single-stack form of <see cref="DropOf"/> kept for callers that only need the item identity.</summary>
    public static StackId DropFor(int blockRuntimeId) => DropOf(blockRuntimeId).Id;
}

/// <summary>A drop that only sometimes happens, with a count range from <c>Drop.Count</c> to <paramref name="MaxCount"/>.</summary>
readonly record struct ChancedDrop(BlockDrop Drop, int MaxCount, float Chance)
{
    public bool Rolls(Random random) => Drop.Count > 0 && (Chance >= 1f || random.NextSingle() < Chance);

    public BlockDrop Draw(Random random) =>
        new(Drop.Id, MaxCount > Drop.Count ? random.Next(Drop.Count, MaxCount + 1) : Drop.Count);
}

/// <summary>One block family's whole drop rule: the main drop and its highest count, what can replace it, what can come with it.</summary>
readonly record struct ResolvedDrop(BlockDrop Main, int MainMax, ChancedDrop[] Instead, ChancedDrop[] Bonus);

/// <summary>A broken block's yield; <see cref="Count"/> 0 means nothing drops.</summary>
readonly record struct BlockDrop(StackId Id, int Count);
