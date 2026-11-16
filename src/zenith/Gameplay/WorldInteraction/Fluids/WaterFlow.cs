using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fluids;

/// <summary>
/// How water moves (ADR §171). A water cell is evaluated <see cref="TickDelay"/> ticks after it, or a neighbour,
/// changed; each evaluation either re-derives the cell's level from what feeds it, or spreads it downward and sideways.
/// Levels are Bedrock's <c>liquid_depth</c>: 0 is a source, 1..<see cref="MaxFlowDepth"/> flows thinner, and
/// <see cref="Falling"/> is added to water that is falling. Every write is a normal world write, so the
/// block-update queue wakes the neighbours, and it is reported in the change list the clients are sent. Chunks that are not resident are never
/// loaded and never flowed into.
/// </summary>
sealed class WaterFlow(World.World world, List<(int X, int Y, int Z, int BlockRuntimeId)> changes)
{
    /// <summary>Game ticks between a change and the water reacting to it (Bedrock's water rate).</summary>
    public const int TickDelay = 5;

    public const int MaxFlowDepth = 7;
    public const int Falling = 8;

    private static readonly (int Dx, int Dz)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly BlockStateTable _states = Blocks.Catalog.States;

    public static bool IsWater(int blockRuntimeId) => Blocks.IsFluid(blockRuntimeId);

    public void Tick(int x, int y, int z)
    {
        if (!world.TryGetBlock(x, y, z, out var id) || !IsWater(id)) return;
        var depth = DepthOf(id);

        if (depth != 0)
        {
            var wanted = Derive(x, y, z);
            if (wanted == None) { Write(x, y, z, World.World.AirRuntimeId); return; }
            if (wanted != depth) { Write(x, y, z, WaterAt(wanted)); return; }
        }

        var flowedDown = SpreadDown(x, y, z);
        if (depth == 0 || !flowedDown) SpreadSideways(x, y, z, depth);
    }

    private const int None = -1;

    /// <summary>The level a flowing cell should have given what feeds it, or <see cref="None"/> when nothing does.</summary>
    private int Derive(int x, int y, int z)
    {
        if (world.TryGetBlock(x, y + 1, z, out var above) && IsWater(above)) return Falling;

        var best = int.MaxValue;
        var sources = 0;
        foreach (var (dx, dz) in Sides)
        {
            if (!world.TryGetBlock(x + dx, y, z + dz, out var side) || !IsWater(side)) continue;
            var depth = DepthOf(side);
            if (depth == 0) sources++;
            best = Math.Min(best, Reach(depth) + 1);
        }

        // Two sources beside open ground make a third: the infinite-water rule.
        if (sources >= 2 && world.TryGetBlock(x, y - 1, z, out var below) && (IsSolid(below) || (IsWater(below) && DepthOf(below) == 0)))
            return 0;

        return best <= MaxFlowDepth ? best : None;
    }

    /// <summary>Water falls into open space beneath it; true when there is such space (or water already there).</summary>
    private bool SpreadDown(int x, int y, int z)
    {
        if (!world.TryGetBlock(x, y - 1, z, out var below)) return true; // unknown below: do not spill sideways over a hole we cannot see
        if (IsWater(below))
        {
            if (DepthOf(below) is not (0 or Falling)) Write(x, y - 1, z, WaterAt(Falling));
            return true;
        }

        if (!CanFlowInto(below)) return false;
        Write(x, y - 1, z, WaterAt(Falling));
        return true;
    }

    private void SpreadSideways(int x, int y, int z, int depth)
    {
        var next = Reach(depth) + 1;
        if (next > MaxFlowDepth) return;

        foreach (var (dx, dz) in Sides)
        {
            var (nx, nz) = (x + dx, z + dz);
            if (!world.TryGetBlock(nx, y, nz, out var target)) continue;
            if (IsWater(target))
            {
                var current = DepthOf(target);
                if (current != 0 && current < Falling && current > next) Write(nx, y, nz, WaterAt(next));
            }
            else if (CanFlowInto(target))
            {
                Write(nx, y, nz, WaterAt(next));
            }
        }
    }

    /// <summary>How far a cell reaches sideways from: a source and falling water both count as full strength.</summary>
    private static int Reach(int depth) => depth is 0 or >= Falling ? 0 : depth;

    private int DepthOf(int id) => _states.TryGet(id, StateKeys.LiquidDepth, out var depth) ? depth : 0;

    private int WaterAt(int depth)
    {
        var id = Blocks.Water;
        return _states.TrySet(ref id, StateKeys.LiquidDepth, depth) ? id : Blocks.Water;
    }

    /// <summary>Air and ground-cover plants give way to water; anything else holds it back.</summary>
    private static bool CanFlowInto(int id) => Blocks.IsAir(id) || Blocks.Catalog.KindOf(id) == BlockKind.Plant;

    private static bool IsSolid(int id) => !Blocks.IsAir(id) && !IsWater(id) && !Blocks.IsPassableDecoration(id);

    private void Write(int x, int y, int z, int id)
    {
        if (world.TrySetBlock(x, y, z, id)) changes.Add((x, y, z, id));
    }
}
