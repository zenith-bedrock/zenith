namespace Zenith.World;

/// <summary>
/// What a block does when something next to it changes (ADR §164). Today that is one question — "has this block lost
/// what it hangs on?" — answered per <see cref="BlockKind"/>. Rules stay data-like and pure: they read the world and
/// say yes or no; the gameplay tick performs the break. A real behaviour registry arrives with the first block that
/// needs more than this (liquid flow).
/// </summary>
static class BlockBehaviors
{
    /// <summary>True when the block at the cell can no longer stand where it is and must break.</summary>
    public static bool LostSupport(World world, int x, int y, int z, int runtimeId)
    {
        switch (Blocks.Catalog.KindOf(runtimeId))
        {
            case BlockKind.Plant:
                return !BlockPlacement.HasSupport(world, x, y, z, runtimeId);

            case BlockKind.Torch:
                return TorchLostWall(world, x, y, z, runtimeId);

            case BlockKind.Door:
                return DoorLostPartOrGround(world, x, y, z, runtimeId);

            default:
                return false;
        }
    }

    /// <summary>The upper half of a door never drops anything of its own; the lower half carries the item.</summary>
    public static bool IsDoorUpperHalf(int runtimeId) =>
        Blocks.Catalog.KindOf(runtimeId) == BlockKind.Door &&
        Blocks.Catalog.Palette.TryGetState(runtimeId, out var state) && state.Get("upper_block_bit") == "1";

    // BlockPlacement stores the clicked face as torch_facing_direction: a torch placed on the north face of a block is
    // "north" and sits one cell north of it, so the supporting block is on the opposite side of the named direction.
    // This is Zenith's own placement convention, not yet checked against a rendered client.
    private static bool TorchLostWall(World world, int x, int y, int z, int runtimeId)
    {
        if (!Blocks.Catalog.Palette.TryGetState(runtimeId, out var state)) return false;
        var (dx, dy, dz) = state.Get("torch_facing_direction") switch
        {
            "top" => (0, -1, 0),
            "north" => (0, 0, 1),
            "south" => (0, 0, -1),
            "west" => (1, 0, 0),
            "east" => (-1, 0, 0),
            _ => (0, 0, 0) // unknown value: leave the torch alone rather than guess
        };
        if (dx == 0 && dy == 0 && dz == 0) return false;
        return !IsSolidSupport(world.GetBlock(x + dx, y + dy, z + dz));
    }

    private static bool DoorLostPartOrGround(World world, int x, int y, int z, int runtimeId)
    {
        var palette = Blocks.Catalog.Palette;
        if (!palette.TryGetName(runtimeId, out var name)) return false;

        if (IsDoorUpperHalf(runtimeId))
        {
            var below = world.GetBlock(x, y - 1, z);
            return !(palette.TryGetName(below, out var belowName) && belowName == name && !IsDoorUpperHalf(below));
        }

        return !IsSolidSupport(world.GetBlock(x, y - 1, z));
    }

    private static bool IsSolidSupport(int runtimeId) =>
        runtimeId != World.AirRuntimeId && !Blocks.IsFluid(runtimeId) && !Blocks.IsPassableDecoration(runtimeId);
}
