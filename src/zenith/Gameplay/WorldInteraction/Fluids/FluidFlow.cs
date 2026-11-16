using Zenith.Gameplay.Policy;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fluids;

/// <summary>The numbers that make one liquid behave differently from another.</summary>
/// <param name="TickDelay">Game ticks between a change and the liquid reacting to it.</param>
/// <param name="SlopeReach">How far, in blocks, it looks for a drop-off to run towards.</param>
/// <param name="DropOff">Levels lost per block of horizontal spread (1 reaches seven blocks, 2 reaches three).</param>
/// <param name="InfiniteSource">Whether two sources beside a gap make a third.</param>
readonly record struct FluidSpec(FluidType Type, int TickDelay, int SlopeReach, int DropOff, bool InfiniteSource)
{
    /// <summary>Overworld water: fast, far, and renewable.</summary>
    public static FluidSpec Water => new(FluidType.Water, TickDelay: 5, SlopeReach: 4, DropOff: 1, InfiniteSource: true);

    /// <summary>Overworld lava: slow, short, and finite.</summary>
    public static FluidSpec Lava => new(FluidType.Lava, TickDelay: 30, SlopeReach: 2, DropOff: 2, InfiniteSource: false);

    public static FluidSpec For(FluidType type) => type == FluidType.Lava ? Lava : Water;
}

/// <summary>
/// How a liquid moves (ADR §171), following the vanilla fluid algorithm. A liquid cell is evaluated
/// <see cref="FluidSpec.TickDelay"/> ticks after it, or a neighbour, changed. A flowing cell first re-derives its own
/// level from what feeds it (<see cref="Derive"/>); then it spreads: down into open space (and not sideways, unless three
/// sources touch it), otherwise sideways, and sideways only towards the nearest drop-off within
/// <see cref="FluidSpec.SlopeReach"/> blocks (all four ways when there is none). Spreading only ever fills cells that are
/// not liquid; liquid that is already there re-derives itself when woken. Levels are Bedrock's <c>liquid_depth</c>:
/// 0 is a source, thinner flows have a higher number, and <see cref="Falling"/> marks falling liquid. Lava also reacts
/// with water (<see cref="TryReact"/>). Every write is an ordinary world write, so the block-update queue wakes the
/// neighbours and the change goes out to clients. Chunks that are not resident are never read as open, flowed into or
/// loaded.
/// <para/>
/// Every change is asked of the environment policies first (cause <see cref="EnvironmentCause.FluidFlow"/>), in the predicates
/// that decide whether a cell can take liquid, so a vetoed cell behaves like a wall: nothing is written, nothing is dropped, and
/// nothing is left waiting for it. A vetoed cell that already holds liquid is simply left as it is.
/// </summary>
sealed class FluidFlow
{
    public const int Falling = 8;
    private const int None = -1;
    private const int Unreachable = 1000;
    private const int SourceAmount = 8;

    // Index ^ 1 is the opposite direction.
    private static readonly (int Dx, int Dz)[] Sides = BlockFaces.Horizontal;

    private readonly World.World _world;
    private readonly FluidSpec _spec;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _changes;
    private readonly Action<int, int, int, int> _beforeDestroy;
    private readonly BlockStateTable _states = Blocks.Catalog.States;
    private readonly EnvironmentChangePolicies _policies;
    private readonly int _sourceBlock;

    /// <param name="changes">Receives every write, for the clients.</param>
    /// <param name="beforeDestroy">Called with (x, y, z, block) before liquid replaces a block that is not air, so it can drop its items.</param>
    public FluidFlow(
        World.World world,
        FluidSpec spec,
        List<(int X, int Y, int Z, int BlockRuntimeId)> changes,
        Action<int, int, int, int> beforeDestroy,
        EnvironmentChangePolicies policies)
    {
        _policies = policies;
        _world = world;
        _spec = spec;
        _changes = changes;
        _beforeDestroy = beforeDestroy;
        _sourceBlock = spec.Type == FluidType.Lava ? Blocks.Lava : Blocks.Water;
    }

    public FluidSpec Spec => _spec;

    /// <summary>The game tick being processed, set by the system that drives this flow; carried into every proposal.</summary>
    public ulong Now { get; set; }

    private bool Permits(int x, int y, int z, int before, int after) =>
        _policies.Allows(EnvironmentCause.FluidFlow, Now, x, y, z, before, after);

    public void Tick(int x, int y, int z)
    {
        if (!_world.TryGetBlock(x, y, z, out var id) || !IsOwn(id)) return;
        if (TryReact(x, y, z, id)) return;

        var depth = FluidBlocks.DepthOf(id);
        if (depth != 0)
        {
            var wanted = Derive(x, y, z);
            if (wanted == None)
            {
                if (Permits(x, y, z, id, World.World.AirRuntimeId)) Write(x, y, z, World.World.AirRuntimeId);
                return;
            }

            if (wanted != depth && Permits(x, y, z, id, LiquidAt(wanted)))
            {
                Write(x, y, z, LiquidAt(wanted));
                depth = wanted;
            }
        }

        Spread(x, y, z, depth);
    }

    /// <summary>
    /// Lava next to water sets: a source becomes obsidian, flowing lava cobblestone. Water on any side but below counts.
    /// Returns true when the cell was changed. Water does nothing of its own here.
    /// </summary>
    public bool TryReact(int x, int y, int z, int id)
    {
        if (_spec.Type != FluidType.Lava) return false;

        if (!TouchesWater(x, y, z)) return false;

        var set = FluidBlocks.IsSource(id) ? Blocks.Obsidian : Blocks.Cobblestone;
        if (!Permits(x, y, z, id, set)) return false;
        Write(x, y, z, set);
        return true;
    }

    private bool Touches(int x, int y, int z) =>
        _world.TryGetBlock(x, y, z, out var id) && Blocks.FluidOf(id) == FluidType.Water;

    private bool IsOwn(int id) => Blocks.FluidOf(id) == _spec.Type;

    /// <summary>The level a cell should have given what feeds it, or <see cref="None"/> when nothing does.</summary>
    private int Derive(int x, int y, int z)
    {
        var strongest = 0;
        var sources = 0;
        foreach (var (dx, dz) in Sides)
        {
            if (!_world.TryGetBlock(x + dx, y, z + dz, out var side) || !IsOwn(side)) continue;
            var depth = FluidBlocks.DepthOf(side);
            if (depth == 0) sources++;
            strongest = Math.Max(strongest, AmountOf(depth));
        }

        // Two sources beside it, over solid ground or another source: a new source (the infinite-water rule).
        if (_spec.InfiniteSource && sources >= 2 && _world.TryGetBlock(x, y - 1, z, out var below) &&
            (IsSolid(below) || (IsOwn(below) && FluidBlocks.DepthOf(below) == 0)))
            return 0;

        if (_world.TryGetBlock(x, y + 1, z, out var above) && IsOwn(above)) return Falling;

        var amount = strongest - _spec.DropOff;
        return amount <= 0 ? None : SourceAmount - amount;
    }

    private void Spread(int x, int y, int z, int depth)
    {
        if (!_world.TryGetBlock(x, y - 1, z, out var below)) return; // a hole we cannot see: do not spill sideways over it

        // Lava falling onto water turns the water cell to stone.
        if (_spec.Type == FluidType.Lava && Blocks.FluidOf(below) == FluidType.Water)
        {
            if (Permits(x, y - 1, z, below, Blocks.Stone)) Write(x, y - 1, z, Blocks.Stone);
            return;
        }

        if (CanSpreadTo(x, y - 1, z, below))
        {
            Place(x, y - 1, z, below, Derive(x, y - 1, z));
            if (SourceNeighbours(x, y, z) >= 3) SpreadSideways(x, y, z, depth);
            return;
        }

        if (depth == 0 || !IsHole(x, y - 1, z, below)) SpreadSideways(x, y, z, depth);
    }

    private void SpreadSideways(int x, int y, int z, int depth)
    {
        var amount = (depth >= Falling ? SourceAmount : AmountOf(depth)) - _spec.DropOff;
        if (amount <= 0) return;

        Span<int> distance = stackalloc int[Sides.Length];
        SlopeDistances(x, y, z, distance);

        var best = Unreachable;
        foreach (var d in distance)
            if (d != NoWay) best = Math.Min(best, d);

        // Only the directions tied for the nearest drop-off get liquid.
        for (var i = 0; i < Sides.Length; i++)
        {
            if (distance[i] != best) continue;
            var (nx, nz) = (x + Sides[i].Dx, z + Sides[i].Dz);
            if (!_world.TryGetBlock(nx, y, nz, out var target) || !CanSpreadTo(nx, y, nz, target)) continue;
            Place(nx, y, nz, target, Derive(nx, y, nz));
        }
    }

    private const int NoWay = -1;

    /// <summary>
    /// For each side of the spreading cell, how many steps the nearest drop-off is that way (0: the neighbour itself is one),
    /// <see cref="Unreachable"/> when none is within <see cref="FluidSpec.SlopeReach"/> steps, <see cref="NoWay"/> when the
    /// liquid cannot go that way at all. One breadth-first walk from all four neighbours at once, each cell read once and
    /// tagged with the sides whose shortest path reaches it, so the cost is the area in reach, not the number of paths.
    /// </summary>
    private void SlopeDistances(int originX, int y, int originZ, Span<int> distance)
    {
        const int MaxReach = 8;
        var reach = Math.Min(_spec.SlopeReach, MaxReach);
        var side = 2 * (reach + 1) + 1;
        var cells = side * side;
        Span<byte> reached = stackalloc byte[cells]; // bit i: the shortest path from side i got here; 0: not seen
        Span<byte> kind = stackalloc byte[cells];    // 0 unread, 1 blocked, 2 open, 3 drop-off
        Span<byte> layer = stackalloc byte[cells];   // the step at which the cell was first reached
        Span<(int X, int Z)> frontier = stackalloc (int, int)[cells];
        Span<(int X, int Z)> next = stackalloc (int, int)[cells];

        reached[Slot(originX, originZ, originX, originZ, reach, side)] = 0xFF; // never walk back through the origin
        var count = 0;
        for (var i = 0; i < Sides.Length; i++)
        {
            var (nx, nz) = (originX + Sides[i].Dx, originZ + Sides[i].Dz);
            switch (Classify(kind, nx, y, nz, originX, originZ, reach, side))
            {
                case 1:
                    distance[i] = NoWay;
                    break;
                case 3:
                    distance[i] = 0;
                    break;
                default:
                    distance[i] = Unreachable;
                    reached[Slot(nx, nz, originX, originZ, reach, side)] |= (byte)(1 << i);
                    frontier[count++] = (nx, nz);
                    break;
            }
        }

        for (var steps = 1; steps <= reach && count > 0; steps++)
        {
            var nextCount = 0;
            for (var f = 0; f < count; f++)
            {
                var (fx, fz) = frontier[f];
                var mask = reached[Slot(fx, fz, originX, originZ, reach, side)];
                foreach (var (dx, dz) in Sides)
                {
                    var (nx, nz) = (fx + dx, fz + dz);
                    var slot = Slot(nx, nz, originX, originZ, reach, side);
                    var known = reached[slot];
                    if (known == 0xFF) continue; // the origin
                    if (known != 0)
                    {
                        // Reached in this very step through another cell: the sides that got there share it.
                        if (layer[slot] == steps) reached[slot] |= mask;
                        continue;
                    }

                    if (Classify(kind, nx, y, nz, originX, originZ, reach, side) == 1) continue;
                    reached[slot] = mask;
                    layer[slot] = (byte)steps;
                    next[nextCount++] = (nx, nz);
                }
            }

            // A drop-off is where the walk ends; it counts for every side whose shortest path arrived.
            for (var n = 0; n < nextCount; n++)
            {
                var slot = Slot(next[n].X, next[n].Z, originX, originZ, reach, side);
                if (kind[slot] != 3) continue;
                for (var i = 0; i < Sides.Length; i++)
                    if ((reached[slot] & (1 << i)) != 0 && distance[i] == Unreachable) distance[i] = steps;
            }

            // Only open cells are walked on from; drop-offs are not.
            var write = 0;
            for (var n = 0; n < nextCount; n++)
                if (kind[Slot(next[n].X, next[n].Z, originX, originZ, reach, side)] == 2) frontier[write++] = next[n];
            count = write;
        }
    }

    private static int Slot(int cx, int cz, int originX, int originZ, int reach, int side) =>
        (cx - originX + reach + 1) * side + (cz - originZ + reach + 1);

    /// <summary>1 blocked, 2 open, 3 drop-off; a cell is read once and the answer kept in <paramref name="kind"/>.</summary>
    private byte Classify(Span<byte> kind, int cx, int y, int cz, int originX, int originZ, int reach, int side)
    {
        var slot = Slot(cx, cz, originX, originZ, reach, side);
        if (kind[slot] != 0) return kind[slot];
        kind[slot] = !_world.TryGetBlock(cx, y, cz, out var cell) || !CanPassThrough(cx, y, cz, cell) ? (byte)1 : IsHoleBelow(cx, y, cz) ? (byte)3 : (byte)2;
        return kind[slot];
    }

    /// <summary>A cell the liquid would fall out of: the one under it is the same liquid, or open, and the environment lets it change.</summary>
    private bool IsHoleBelow(int x, int y, int z) =>
        _world.TryGetBlock(x, y - 1, z, out var below) && IsHole(x, y - 1, z, below);

    private bool IsHole(int x, int y, int z, int id) => (IsOwn(id) || CanHold(id)) && Permits(x, y, z, id, _sourceBlock);

    private int SourceNeighbours(int x, int y, int z)
    {
        var count = 0;
        foreach (var (dx, dz) in Sides)
            if (_world.TryGetBlock(x + dx, y, z + dz, out var side) && IsOwn(side) && FluidBlocks.DepthOf(side) == 0) count++;
        return count;
    }

    /// <summary>Amount (source 8, thinning to 1; falling counts as a source) — the reverse of depth.</summary>
    private static int AmountOf(int depth) => depth is 0 or >= Falling ? SourceAmount : SourceAmount - depth;

    private int LiquidAt(int depth)
    {
        var id = _sourceBlock;
        return _states.TrySet(ref id, StateKeys.LiquidDepth, depth) ? id : _sourceBlock;
    }

    /// <summary>Open space or something liquid washes away: air, ground-cover plants and torches.</summary>
    public static bool CanHold(int id) =>
        Blocks.IsAir(id) || Blocks.IsFire(id) || Blocks.Catalog.KindOf(id) is BlockKind.Plant or BlockKind.Torch;

    /// <summary>Liquid can be put here: it is open and not already liquid (liquid that is there re-derives itself).</summary>
    private bool CanSpreadTo(int x, int y, int z, int id) => !Blocks.IsFluid(id) && CanHold(id) && Permits(x, y, z, id, _sourceBlock);

    /// <summary>Liquid can run across this cell on its way to a drop-off: open, or its own flowing liquid.</summary>
    private bool CanPassThrough(int x, int y, int z, int id) =>
        IsOwn(id) ? FluidBlocks.DepthOf(id) != 0 : !Blocks.IsFluid(id) && CanHold(id) && Permits(x, y, z, id, _sourceBlock);

    private static bool IsSolid(int id) => !Blocks.IsAir(id) && !Blocks.IsFluid(id) && !Blocks.IsFire(id) && !Blocks.IsPassableDecoration(id);

    private void Place(int x, int y, int z, int existing, int depth)
    {
        if (depth == None) return;
        if (!Blocks.IsAir(existing)) _beforeDestroy(x, y, z, existing);
        Write(x, y, z, LiquidAt(depth));
    }

    private void Write(int x, int y, int z, int id)
    {
        if (_world.TrySetBlock(x, y, z, id)) _changes.Add((x, y, z, id));
    }

    private bool TouchesWater(int x, int y, int z) =>
        Touches(x, y + 1, z) || Touches(x + 1, y, z) || Touches(x - 1, y, z) || Touches(x, y, z + 1) || Touches(x, y, z - 1);
}

/// <summary>Reading liquid levels off block ids.</summary>
static class FluidBlocks
{
    /// <summary>The block's <c>liquid_depth</c> (0 for a source, and for anything that is not liquid).</summary>
    public static int DepthOf(int id) =>
        Blocks.Catalog.States.TryGet(id, StateKeys.LiquidDepth, out var depth) ? depth : 0;

    /// <summary>True for a liquid source (water or lava at depth 0).</summary>
    public static bool IsSource(int id) => Blocks.IsFluid(id) && DepthOf(id) == 0;
}
