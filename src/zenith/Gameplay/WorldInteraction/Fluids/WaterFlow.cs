using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fluids;

/// <summary>
/// How water moves (ADR §171), following the vanilla fluid algorithm. A water cell is evaluated
/// <see cref="TickDelay"/> ticks after it, or a neighbour, changed. A flowing cell first re-derives its own level from
/// what feeds it (<see cref="Derive"/>); then it spreads: down into open space (and not sideways, unless three sources
/// touch it), otherwise sideways, and sideways only towards the nearest drop-off within <see cref="SlopeReach"/> blocks
/// (all four ways when there is none). Spreading only ever fills cells that are not water; water that is already there
/// re-derives itself when it is woken. Levels are Bedrock's <c>liquid_depth</c>: 0 is a source, 1..<see cref="MaxFlowDepth"/>
/// flows thinner, and <see cref="Falling"/> marks falling water. Every write is an ordinary world write, so the
/// block-update queue wakes the neighbours and the change goes out to clients. Chunks that are not resident are never
/// read as open, flowed into or loaded.
/// </summary>
sealed class WaterFlow
{
    /// <summary>Game ticks between a change and the water reacting to it.</summary>
    public const int TickDelay = 5;

    public const int MaxFlowDepth = 7;
    public const int Falling = 8;

    /// <summary>How far, in blocks, water looks for a drop-off to run towards.</summary>
    public const int SlopeReach = 4;

    private const int None = -1;
    private const int Unreachable = 1000;
    private const int SourceAmount = 8;

    // Index ^ 1 is the opposite direction.
    private static readonly (int Dx, int Dz)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly World.World _world;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _changes;
    private readonly Action<int, int, int, int> _beforeDestroy;
    private readonly BlockStateTable _states = Blocks.Catalog.States;

    /// <param name="changes">Receives every write, for the clients.</param>
    /// <param name="beforeDestroy">Called with (x, y, z, block) before water replaces a block that is not air, so it can drop its items.</param>
    public WaterFlow(
        World.World world,
        List<(int X, int Y, int Z, int BlockRuntimeId)> changes,
        Action<int, int, int, int> beforeDestroy)
    {
        _world = world;
        _changes = changes;
        _beforeDestroy = beforeDestroy;
    }

    public static bool IsWater(int blockRuntimeId) => Blocks.IsFluid(blockRuntimeId);

    /// <summary>True for a water source (depth 0).</summary>
    public static bool IsSource(int blockRuntimeId) =>
        IsWater(blockRuntimeId) && (!Blocks.Catalog.States.TryGet(blockRuntimeId, StateKeys.LiquidDepth, out var depth) || depth == 0);

    /// <summary>A bucket of water can be emptied here: open space, a washable plant, or flowing water (not a source).</summary>
    public static bool CanTakeSource(int blockRuntimeId) => IsWater(blockRuntimeId) ? !IsSource(blockRuntimeId) : CanHold(blockRuntimeId);

    public void Tick(int x, int y, int z)
    {
        if (!_world.TryGetBlock(x, y, z, out var id) || !IsWater(id)) return;
        var depth = DepthOf(id);

        if (depth != 0)
        {
            var wanted = Derive(x, y, z);
            if (wanted == None)
            {
                Write(x, y, z, World.World.AirRuntimeId);
                return;
            }

            if (wanted != depth)
            {
                Write(x, y, z, WaterAt(wanted));
                depth = wanted;
            }
        }

        Spread(x, y, z, depth);
    }

    /// <summary>The level a cell should have given what feeds it, or <see cref="None"/> when nothing does.</summary>
    private int Derive(int x, int y, int z)
    {
        var strongest = 0;
        var sources = 0;
        foreach (var (dx, dz) in Sides)
        {
            if (!_world.TryGetBlock(x + dx, y, z + dz, out var side) || !IsWater(side)) continue;
            var depth = DepthOf(side);
            if (depth == 0) sources++;
            strongest = Math.Max(strongest, AmountOf(depth));
        }

        // Two sources beside it, over solid ground or another source: a new source (the infinite-water rule).
        if (sources >= 2 && _world.TryGetBlock(x, y - 1, z, out var below) &&
            (IsSolid(below) || (IsWater(below) && DepthOf(below) == 0)))
            return 0;

        if (_world.TryGetBlock(x, y + 1, z, out var above) && IsWater(above)) return Falling;

        var amount = strongest - 1;
        return amount <= 0 ? None : SourceAmount - amount;
    }

    private void Spread(int x, int y, int z, int depth)
    {
        if (!_world.TryGetBlock(x, y - 1, z, out var below)) return; // a hole we cannot see: do not spill sideways over it

        if (CanSpreadTo(below))
        {
            Place(x, y - 1, z, below, Derive(x, y - 1, z));
            if (SourceNeighbours(x, y, z) >= 3) SpreadSideways(x, y, z, depth);
            return;
        }

        if (depth == 0 || !IsHole(below)) SpreadSideways(x, y, z, depth);
    }

    private void SpreadSideways(int x, int y, int z, int depth)
    {
        var amount = depth >= Falling ? MaxFlowDepth : AmountOf(depth) - 1;
        if (amount <= 0) return;

        Span<int> distance = stackalloc int[Sides.Length];
        var best = Unreachable;
        for (var i = 0; i < Sides.Length; i++)
        {
            distance[i] = None;
            var (nx, nz) = (x + Sides[i].Dx, z + Sides[i].Dz);
            if (!_world.TryGetBlock(nx, y, nz, out var target) || !CanPassThrough(target)) continue;

            distance[i] = IsHole(nx, y, nz) ? 0 : SlopeDistance(nx, y, nz, 1, i ^ 1);
            best = Math.Min(best, distance[i]);
        }

        // Only the directions tied for the nearest drop-off get water.
        for (var i = 0; i < Sides.Length; i++)
        {
            if (distance[i] != best) continue;
            var (nx, nz) = (x + Sides[i].Dx, z + Sides[i].Dz);
            if (!_world.TryGetBlock(nx, y, nz, out var target) || !CanSpreadTo(target)) continue;
            Place(nx, y, nz, target, Derive(nx, y, nz));
        }
    }

    /// <summary>Steps to the nearest drop-off reachable from a cell (not going back the way it came), or <see cref="Unreachable"/>.</summary>
    private int SlopeDistance(int x, int y, int z, int steps, int cameFrom)
    {
        var best = Unreachable;
        for (var i = 0; i < Sides.Length; i++)
        {
            if (i == cameFrom) continue;
            var (nx, nz) = (x + Sides[i].Dx, z + Sides[i].Dz);
            if (!_world.TryGetBlock(nx, y, nz, out var next) || !CanPassThrough(next)) continue;

            if (IsHole(nx, y, nz)) return steps;
            if (steps < SlopeReach) best = Math.Min(best, SlopeDistance(nx, y, nz, steps + 1, i ^ 1));
        }

        return best;
    }

    /// <summary>A cell water would fall out of: the one under it is water, or open.</summary>
    private bool IsHole(int x, int y, int z) =>
        _world.TryGetBlock(x, y - 1, z, out var below) && IsHole(below);

    private static bool IsHole(int below) => IsWater(below) || CanHold(below);

    private int SourceNeighbours(int x, int y, int z)
    {
        var count = 0;
        foreach (var (dx, dz) in Sides)
            if (_world.TryGetBlock(x + dx, y, z + dz, out var side) && IsWater(side) && DepthOf(side) == 0) count++;
        return count;
    }

    /// <summary>Water's amount (source 8, thinning to 1; falling water counts as a source) — the reverse of its depth.</summary>
    private static int AmountOf(int depth) => depth is 0 or >= Falling ? SourceAmount : SourceAmount - depth;

    private int DepthOf(int id) => _states.TryGet(id, StateKeys.LiquidDepth, out var depth) ? depth : 0;

    private int WaterAt(int depth)
    {
        var id = Blocks.Water;
        return _states.TrySet(ref id, StateKeys.LiquidDepth, depth) ? id : Blocks.Water;
    }

    /// <summary>Open space or something water washes away: air and ground-cover plants and torches.</summary>
    public static bool CanHold(int id) =>
        Blocks.IsAir(id) || Blocks.Catalog.KindOf(id) is BlockKind.Plant or BlockKind.Torch;

    /// <summary>Water can be put here: it is open and not already water (existing water re-derives itself).</summary>
    private static bool CanSpreadTo(int id) => !IsWater(id) && CanHold(id);

    /// <summary>Water can run across this cell on its way to a drop-off: open, or water that is not a source.</summary>
    private bool CanPassThrough(int id) => IsWater(id) ? DepthOf(id) != 0 : CanHold(id);

    private static bool IsSolid(int id) => !Blocks.IsAir(id) && !IsWater(id) && !Blocks.IsPassableDecoration(id);

    private void Place(int x, int y, int z, int existing, int depth)
    {
        if (depth == None) return;
        if (!Blocks.IsAir(existing)) _beforeDestroy(x, y, z, existing);
        Write(x, y, z, WaterAt(depth));
    }

    private void Write(int x, int y, int z, int id)
    {
        if (_world.TrySetBlock(x, y, z, id)) _changes.Add((x, y, z, id));
    }
}
