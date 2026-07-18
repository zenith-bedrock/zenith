namespace Zenith.World;

/// <summary>
/// Double-chest pair geometry (ADR §56). No BlockActor — adjacency + same facing only.
/// </summary>
readonly record struct ChestPair(
    int PrimaryX,
    int PrimaryY,
    int PrimaryZ,
    int PartnerX,
    int PartnerY,
    int PartnerZ)
{
    public (int X, int Y, int Z) Primary => (PrimaryX, PrimaryY, PrimaryZ);
    public (int X, int Y, int Z) Partner => (PartnerX, PartnerY, PartnerZ);
}

/// <summary>Open UI view over one or two chest cells (ADR §56).</summary>
readonly record struct OpenChestView(
    int PrimaryX,
    int PrimaryY,
    int PrimaryZ,
    int? PartnerX,
    int? PartnerY,
    int? PartnerZ,
    int SlotCount)
{
    public static OpenChestView Single(int x, int y, int z) =>
        new(x, y, z, null, null, null, ChestStore.SingleSize);

    public static OpenChestView Double(in ChestPair pair) =>
        new(
            pair.PrimaryX, pair.PrimaryY, pair.PrimaryZ,
            pair.PartnerX, pair.PartnerY, pair.PartnerZ,
            ChestStore.DoubleSize);

    public bool IsDouble => SlotCount == ChestStore.DoubleSize && PartnerX.HasValue;

    public (int X, int Y, int Z) Primary => (PrimaryX, PrimaryY, PrimaryZ);

    public bool TryGetPartner(out int x, out int y, out int z)
    {
        if (!IsDouble)
        {
            x = y = z = 0;
            return false;
        }

        x = PartnerX!.Value;
        y = PartnerY!.Value;
        z = PartnerZ!.Value;
        return true;
    }

    /// <summary>True if <paramref name="x"/>,<paramref name="y"/>,<paramref name="z"/> is primary or partner.</summary>
    public bool Contains(int x, int y, int z) =>
        (PrimaryX == x && PrimaryY == y && PrimaryZ == z) ||
        (PartnerX == x && PartnerY == y && PartnerZ == z);
}

/// <summary>Resolve chest pairs from world cells (ADR §56).</summary>
static class ChestPairing
{
    /// <summary>
    /// If <paramref name="x"/>,<paramref name="y"/>,<paramref name="z"/> is a chest with a
    /// same-facing axis neighbor, returns the ordered pair (primary = min X then Z).
    /// </summary>
    public static bool TryResolve(World world, int x, int y, int z, out ChestPair pair)
    {
        pair = default;
        var rid = world.GetBlock(x, y, z);
        if (!Blocks.TryGetChestCardinal(rid, out var facing))
            return false;

        if (!TryPartnerOffset(facing, out var dx, out var dz))
            return false;

        // Prefer +axis neighbor, else −axis (at most one partner).
        if (TryPartnerAt(world, x, y, z, facing, x + dx, y, z + dz, out pair))
            return true;
        if (TryPartnerAt(world, x, y, z, facing, x - dx, y, z - dz, out pair))
            return true;
        return false;
    }

    /// <summary>Build <see cref="OpenChestView"/> for the clicked cell (27 or 54).</summary>
    public static OpenChestView ViewFor(World world, int x, int y, int z)
    {
        if (TryResolve(world, x, y, z, out var pair))
            return OpenChestView.Double(pair);
        return OpenChestView.Single(x, y, z);
    }

    /// <summary>
    /// When placing at <paramref name="x"/>,<paramref name="y"/>,<paramref name="z"/>, adopt a
    /// neighbor chest's facing if that neighbor would pair on its axis (ADR §56).
    /// </summary>
    public static int AlignFacingWithNeighbor(World world, int x, int y, int z, int proposedRid)
    {
        if (TryNeighborFacing(world, x + 1, y, z, pairOnX: true, out var facing) ||
            TryNeighborFacing(world, x - 1, y, z, pairOnX: true, out facing) ||
            TryNeighborFacing(world, x, y, z + 1, pairOnX: false, out facing) ||
            TryNeighborFacing(world, x, y, z - 1, pairOnX: false, out facing))
            return Blocks.ChestForFacing(facing);
        return proposedRid;
    }

    private static bool TryNeighborFacing(
        World world, int nx, int ny, int nz, bool pairOnX, out string facing)
    {
        facing = Blocks.CardinalSouth;
        if (!Blocks.TryGetChestCardinal(world.GetBlock(nx, ny, nz), out facing))
            return false;
        if (pairOnX)
            return facing is Blocks.CardinalNorth or Blocks.CardinalSouth;
        return facing is Blocks.CardinalEast or Blocks.CardinalWest;
    }

    /// <summary>
    /// Pair axis unit step: N/S-facing chests pair on X; E/W-facing on Z.
    /// </summary>
    public static bool TryPartnerOffset(string facing, out int dx, out int dz)
    {
        switch (facing)
        {
            case Blocks.CardinalNorth:
            case Blocks.CardinalSouth:
                dx = 1;
                dz = 0;
                return true;
            case Blocks.CardinalEast:
            case Blocks.CardinalWest:
                dx = 0;
                dz = 1;
                return true;
            default:
                dx = 0;
                dz = 0;
                return false;
        }
    }

    private static bool TryPartnerAt(
        World world,
        int x,
        int y,
        int z,
        string facing,
        int ox,
        int oy,
        int oz,
        out ChestPair pair)
    {
        pair = default;
        var other = world.GetBlock(ox, oy, oz);
        if (!Blocks.TryGetChestCardinal(other, out var otherFacing))
            return false;
        if (!string.Equals(facing, otherFacing, StringComparison.Ordinal))
            return false;

        // Primary = lexicographically smaller (X, Z).
        if (x < ox || (x == ox && z < oz))
            pair = new ChestPair(x, y, z, ox, oy, oz);
        else
            pair = new ChestPair(ox, oy, oz, x, y, z);
        return true;
    }
}
