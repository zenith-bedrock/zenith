using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.RandomTicks;

/// <summary>
/// What one random-ticked block does (ADR §169). The blocks that get ticks are named in data (the <c>RandomTick</c>
/// trait in block_definitions.json); the behaviour is code, registered per block in <see cref="RandomTickBehaviors"/>.
/// </summary>
interface IRandomTickBehavior
{
    void OnRandomTick(RandomTickContext tick);
}

/// <summary>
/// The cell being ticked and the ways to act on the world. One instance is reused for every tick of a frame, so a
/// behaviour must not keep it. Writes go through <see cref="TrySet"/> so that clients are told about them.
/// </summary>
sealed class RandomTickContext(World.World world, Random random, List<(int X, int Y, int Z, int BlockRuntimeId)> changes)
{
    public World.World World { get; } = world;

    public Random Random { get; } = random;

    public int X { get; private set; }
    public int Y { get; private set; }
    public int Z { get; private set; }

    /// <summary>The block at the ticked cell when the tick started.</summary>
    public int BlockRuntimeId { get; private set; }

    public void Aim(int x, int y, int z, int blockRuntimeId)
    {
        X = x;
        Y = y;
        Z = z;
        BlockRuntimeId = blockRuntimeId;
    }

    /// <summary>Reads a block; false when its chunk is not resident (a tick never wakes a chunk).</summary>
    public bool TryGetBlock(int x, int y, int z, out int blockRuntimeId) => World.TryGetBlock(x, y, z, out blockRuntimeId);

    /// <summary>Writes a block and queues it for the clients; false when the write is refused.</summary>
    public bool TrySet(int x, int y, int z, int blockRuntimeId)
    {
        if (!World.TrySetBlock(x, y, z, blockRuntimeId)) return false;
        changes.Add((x, y, z, blockRuntimeId));
        return true;
    }
}

/// <summary>The random-tick behaviours, by block family. Checked against the data at startup.</summary>
sealed class RandomTickBehaviors
{
    private readonly Dictionary<BlockFamily, IRandomTickBehavior> _byFamily = [];

    /// <summary>Every behaviour Zenith ships.</summary>
    public static RandomTickBehaviors Default()
    {
        var behaviors = new RandomTickBehaviors();
        behaviors.Register(Blocks.GrassBlock, new GrassSpreadBehavior());
        return behaviors;
    }

    /// <summary>Registers <paramref name="behavior"/> for the family of <paramref name="blockRuntimeId"/> (all its states).</summary>
    public void Register(int blockRuntimeId, IRandomTickBehavior behavior)
    {
        var family = Blocks.Catalog.States.FamilyOf(blockRuntimeId)
            ?? throw new InvalidOperationException($"Block {blockRuntimeId} has no family to register a random tick for.");
        if (!_byFamily.TryAdd(family, behavior))
            throw new InvalidOperationException($"{family.Name} already has a random-tick behaviour.");
    }

    public bool TryGet(int blockRuntimeId, out IRandomTickBehavior behavior)
    {
        behavior = null!;
        return Blocks.Catalog.States.FamilyOf(blockRuntimeId) is { } family && _byFamily.TryGetValue(family, out behavior!);
    }

    /// <summary>
    /// Fails, listing every problem, when the data and the code disagree: a block marked <c>RandomTick</c> with no
    /// behaviour would be counted as tickable and never do anything; a behaviour on an unmarked block would never run.
    /// </summary>
    public void Validate()
    {
        var problems = new List<string>();
        foreach (var family in Blocks.Catalog.States.Families)
        {
            var marked = (family.Traits & BlockTraits.RandomTick) != 0;
            var registered = _byFamily.ContainsKey(family);
            if (marked && !registered) problems.Add($"{family.Name} has the RandomTick trait but no behaviour");
            if (!marked && registered) problems.Add($"{family.Name} has a random-tick behaviour but not the RandomTick trait");
        }

        if (problems.Count > 0)
            throw new InvalidOperationException("Random ticks are inconsistent: " + string.Join("; ", problems));
    }
}
