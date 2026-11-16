namespace Zenith.World;

/// <summary>
/// World-dependent block shape rules: the second half of a door, wall connections, slab merging and open/close
/// toggling. Everything here is a pure function of the world snapshot that returns the writes to perform — the caller
/// (the gameplay tick) owns the actual mutation, so the single-writer rule holds.
/// </summary>
static class BlockShapes
{
    /// <summary>One cell write produced by a shape rule.</summary>
    public readonly record struct Write(int X, int Y, int Z, int BlockRuntimeId);

    private static readonly (int Dx, int Dz, string Key)[] Sides =
    [
        (0, -1, "wall_connection_type_north"),
        (0, 1, "wall_connection_type_south"),
        (1, 0, "wall_connection_type_east"),
        (-1, 0, "wall_connection_type_west")
    ];

    /// <summary>The state of <paramref name="runtimeId"/> with the given properties replaced; null when it has no such state.</summary>
    public static int? WithState(int runtimeId, params (string Key, string Value)[] overrides)
    {
        var palette = Blocks.Palette;
        if (!palette.TryGetName(runtimeId, out var name) || !palette.TryGetState(runtimeId, out var state)) return null;
        var map = new Dictionary<string, string>(overrides.Length);
        foreach (var (key, value) in overrides) map[key] = value;
        var merged = state.With(map);
        return merged is not null && palette.TryGetExact(name, merged, out var id) ? id : null;
    }

    /// <summary>Whether a placement of <paramref name="runtimeId"/> at the cell is legal for this world (door needs headroom).</summary>
    public static bool CanPlace(World world, int x, int y, int z, int runtimeId, int existing)
    {
        if (IsSlabMerge(existing, runtimeId)) return true;
        // A double slab only ever comes from merging into a single slab, never into open air.
        if (existing != World.AirRuntimeId || Blocks.Catalog.KindOf(runtimeId) == BlockKind.DoubleSlab) return false;
        return Blocks.Catalog.KindOf(runtimeId) != BlockKind.Door || world.GetBlock(x, y + 1, z) == World.AirRuntimeId;
    }

    /// <summary>
    /// Final id for the cell being placed plus any extra writes the placement implies (door upper half, neighbour wall
    /// updates). Returns the id to write at the cell itself.
    /// </summary>
    public static int Finalize(World world, int x, int y, int z, int runtimeId, List<Write> extra)
    {
        switch (Blocks.Catalog.KindOf(runtimeId))
        {
            case BlockKind.Door:
                if (WithState(runtimeId, ("upper_block_bit", "1")) is { } upper)
                    extra.Add(new Write(x, y + 1, z, upper));
                return runtimeId;

            case BlockKind.Wall:
                var wall = ReshapeWall(world, x, y, z, runtimeId);
                RefreshNeighbourWalls(world, x, y, z, extra, selfId: wall);
                return wall;

            default:
                return runtimeId;
        }
    }

    /// <summary>Writes that bring neighbouring walls in line after the cell was cleared.</summary>
    public static void AfterRemoval(World world, int x, int y, int z, int removedRuntimeId, List<Write> extra)
    {
        var kind = Blocks.Catalog.KindOf(removedRuntimeId);
        if (kind == BlockKind.Door)
        {
            // Breaking either half takes the whole door with it.
            foreach (var dy in new[] { 1, -1 })
            {
                var partner = world.GetBlock(x, y + dy, z);
                if (SameDoor(removedRuntimeId, partner))
                    extra.Add(new Write(x, y + dy, z, World.AirRuntimeId));
            }
        }
        RefreshNeighbourWalls(world, x, y, z, extra, selfId: World.AirRuntimeId);
    }

    /// <summary>
    /// Open/close toggle for a right-clicked door, trapdoor or fence gate; false when the block does not toggle by hand
    /// (iron variants answer to redstone only).
    /// </summary>
    public static bool TryToggle(World world, int x, int y, int z, List<Write> writes)
    {
        var id = world.GetBlock(x, y, z);
        var kind = Blocks.Catalog.KindOf(id);
        if (kind is not (BlockKind.Door or BlockKind.Trapdoor or BlockKind.FenceGate)) return false;
        if (!Blocks.Catalog.Palette.TryGetName(id, out var name) || name.StartsWith("minecraft:iron_", StringComparison.Ordinal))
            return false;
        if (!Blocks.Catalog.Palette.TryGetState(id, out var state)) return false;

        var open = state.Get("open_bit") == "1" ? "0" : "1";
        if (WithState(id, ("open_bit", open)) is not { } toggled) return false;
        writes.Add(new Write(x, y, z, toggled));

        if (kind == BlockKind.Door)
        {
            var dy = state.Get("upper_block_bit") == "1" ? -1 : 1;
            var partner = world.GetBlock(x, y + dy, z);
            if (SameDoor(id, partner) && WithState(partner, ("open_bit", open)) is { } partnerToggled)
                writes.Add(new Write(x, y + dy, z, partnerToggled));
        }
        return true;
    }

    /// <summary>The double slab that replaces <paramref name="existing"/> when a second slab joins it, if any.</summary>
    public static bool TryMergeInto(int existing, int placedSlab, bool requireOppositeHalf, int? clickedFace, out int doubleSlab)
    {
        doubleSlab = 0;
        var catalog = Blocks.Catalog;
        if (catalog.KindOf(existing) != BlockKind.Slab || catalog.KindOf(placedSlab) != BlockKind.Slab) return false;
        var palette = catalog.Palette;
        if (!palette.TryGetName(existing, out var existingName) || !palette.TryGetName(placedSlab, out var placedName) ||
            existingName != placedName) return false;
        if (!palette.TryGetState(existing, out var existingState) || !palette.TryGetState(placedSlab, out var placedState)) return false;

        var existingHalf = existingState.Get("minecraft:vertical_half");
        if (requireOppositeHalf && existingHalf == placedState.Get("minecraft:vertical_half")) return false;
        if (clickedFace is { } face && !(face == 1 && existingHalf == "bottom" || face == 0 && existingHalf == "top")) return false;

        var doubleName = existingName[..^"_slab".Length] + "_double_slab";
        return palette.TryGet(doubleName, out doubleSlab);
    }

    /// <summary>True when <paramref name="placed"/> is the double slab that a single <paramref name="existing"/> slab becomes.</summary>
    public static bool IsSlabMerge(int existing, int placed)
    {
        var catalog = Blocks.Catalog;
        if (catalog.KindOf(existing) != BlockKind.Slab || catalog.KindOf(placed) != BlockKind.DoubleSlab) return false;
        var palette = catalog.Palette;
        return palette.TryGetName(existing, out var single) && palette.TryGetName(placed, out var doubled) &&
               doubled == single[..^"_slab".Length] + "_double_slab";
    }

    private static bool SameDoor(int a, int b)
    {
        var palette = Blocks.Catalog.Palette;
        return Blocks.Catalog.KindOf(b) == BlockKind.Door && palette.TryGetName(a, out var na) &&
               palette.TryGetName(b, out var nb) && na == nb;
    }

    private static void RefreshNeighbourWalls(World world, int x, int y, int z, List<Write> extra, int selfId)
    {
        foreach (var (dx, dz, _) in Sides)
        {
            var nx = x + dx;
            var nz = z + dz;
            var neighbour = world.GetBlock(nx, y, nz);
            if (Blocks.Catalog.KindOf(neighbour) != BlockKind.Wall) continue;
            var reshaped = ReshapeWall(world, nx, y, nz, neighbour, overrideCell: (x, y, z, selfId));
            if (reshaped != neighbour) extra.Add(new Write(nx, y, nz, reshaped));
        }
    }

    // A wall connects to walls, fence gates and opaque full blocks; "tall" when something sits on top of the wall.
    private static int ReshapeWall(
        World world, int x, int y, int z, int wallId,
        (int X, int Y, int Z, int Id)? overrideCell = null)
    {
        int Read(int cx, int cy, int cz) =>
            overrideCell is { } o && o.X == cx && o.Y == cy && o.Z == cz ? o.Id : world.GetBlock(cx, cy, cz);

        var aboveId = Read(x, y + 1, z);
        var aboveSolid = aboveId != World.AirRuntimeId && !Blocks.IsFluid(aboveId) && !Blocks.IsPassableDecoration(aboveId);
        var overrides = new List<(string, string)>(5);
        var connected = new bool[4];
        for (var i = 0; i < Sides.Length; i++)
        {
            var (dx, dz, key) = Sides[i];
            connected[i] = ConnectsTo(Read(x + dx, y, z + dz));
            overrides.Add((key, !connected[i] ? "none" : aboveSolid ? "tall" : "short"));
        }

        // Post unless the wall runs straight through (north–south or east–west with nothing else) and is not capped.
        var straight = connected[0] && connected[1] && !connected[2] && !connected[3] ||
                       connected[2] && connected[3] && !connected[0] && !connected[1];
        overrides.Add(("wall_post_bit", straight && !aboveSolid ? "0" : "1"));
        return WithState(wallId, overrides.ToArray()) ?? wallId;
    }

    private static bool ConnectsTo(int id)
    {
        var kind = Blocks.Catalog.KindOf(id);
        if (kind is BlockKind.Wall or BlockKind.FenceGate) return true;
        if (kind is not (BlockKind.Cube or BlockKind.Pillar)) return false;
        if (!Blocks.Catalog.Palette.TryGetName(id, out var name)) return false;
        return Blocks.BlocksMovement(id) && !name.Contains("glass", StringComparison.Ordinal) &&
               !name.Contains("leaves", StringComparison.Ordinal);
    }
}
