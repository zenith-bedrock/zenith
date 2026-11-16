using Zenith.Gameplay.Policy;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.RandomTicks;

/// <summary>
/// An oak sapling matures in two steps: a random tick first sets its growth bit (one in <see cref="OddsPerStage"/>),
/// the next successful one grows a tree, the same shape world-gen plants (trunk, canopy of radius 2 over the top three
/// trunk cells, a cap of radius 1) — provided every cell the tree needs is free and its chunks are resident.
/// <para/>
/// The tree is one planned unit: while planning, every cell is asked of the environment policies (cause Growth) and a single refusal
/// abandons the whole tree, so there is never a trunk without its canopy or a canopy without its trunk.
/// </summary>
sealed class SaplingGrowthBehavior : IRandomTickBehavior
{
    private const int OddsPerStage = 7;
    private const int MinTrunk = 4;
    private const int MaxTrunk = 6;
    private const int CanopyRadius = 2;

    private readonly List<(int X, int Y, int Z, int Block)> _tree = [];

    public void OnRandomTick(RandomTickContext tick)
    {
        if (tick.Random.Next(OddsPerStage) != 0) return;

        var states = Blocks.Catalog.States;
        if (!states.Bit(tick.BlockRuntimeId, StateKeys.Age))
        {
            var grown = tick.BlockRuntimeId;
            if (states.TrySet(ref grown, StateKeys.Age, true)) tick.TrySet(EnvironmentCause.Growth, tick.X, tick.Y, tick.Z, grown);
            return;
        }

        if (!Plan(tick, tick.Random.Next(MinTrunk, MaxTrunk + 1))) return;
        foreach (var (x, y, z, block) in _tree) tick.TrySet(EnvironmentCause.Growth, x, y, z, block);
    }

    /// <summary>Lays the tree out in <see cref="_tree"/>; false when any cell is taken or unknown.</summary>
    private bool Plan(RandomTickContext tick, int trunk)
    {
        _tree.Clear();
        var top = tick.Y + trunk - 1; // the sapling's cell is the trunk's first
        for (var y = tick.Y; y <= top; y++)
            if (!TryAdd(tick, tick.X, y, tick.Z, Blocks.OakLog)) return false;

        for (var y = top - 2; y <= top + 1; y++)
        {
            var radius = y == top + 1 ? 1 : CanopyRadius;
            for (var dx = -radius; dx <= radius; dx++)
            for (var dz = -radius; dz <= radius; dz++)
            {
                if (y == top - 2 && Math.Abs(dx) == CanopyRadius && Math.Abs(dz) == CanopyRadius) continue;
                if (dx == 0 && dz == 0 && y <= top) continue; // the trunk
                if (!TryAdd(tick, tick.X + dx, y, tick.Z + dz, Blocks.OakLeaves)) return false;
            }
        }

        return true;
    }

    private bool TryAdd(RandomTickContext tick, int x, int y, int z, int block)
    {
        if (!tick.TryGetBlock(x, y, z, out var existing)) return false;
        var isSapling = x == tick.X && y == tick.Y && z == tick.Z;
        if (!isSapling && !Blocks.IsAir(existing) && !Blocks.IsLeaves(existing) && !Blocks.IsPassableDecoration(existing)) return false;
        if (!tick.Allows(EnvironmentCause.Growth, x, y, z, existing, block)) return false;
        _tree.Add((x, y, z, block));
        return true;
    }
}
