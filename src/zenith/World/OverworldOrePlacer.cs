using System.Runtime.CompilerServices;

namespace Zenith.World;

/// <summary>
/// Deterministic ore vein clusters for noise overworld (ADR §66).
/// Replaces stone/deepslate hosts only — never grass, dirt, air, or carved cells.
/// </summary>
static class OverworldOrePlacer
{
    internal const int VeinCellSize = 8;
    internal const int VeinRadius = 2;
    internal const int ColumnMinOreY = Blocks.FlatMinY;
    internal const int ColumnMaxOreY = 120;
    internal const int MaxColumnCellCount = 4 * 25 * 4;

    internal const int OreSalt = unchecked((int)0x0EE0E001u);

    /// <summary>Non-zero ore runtime id when <paramref name="hostBlock"/> should be replaced.</summary>
    public static int TryReplaceHost(int hostBlock, int worldX, int worldY, int worldZ, int seed)
    {
        if (hostBlock != Blocks.Stone && hostBlock != Blocks.Deepslate)
            return 0;

        var deep = hostBlock == Blocks.Deepslate;
        var minCellX = FloorDiv(worldX - VeinRadius, VeinCellSize);
        var maxCellX = FloorDiv(worldX + VeinRadius, VeinCellSize);
        var minCellY = FloorDiv(worldY - VeinRadius, VeinCellSize);
        var maxCellY = FloorDiv(worldY + VeinRadius, VeinCellSize);
        var minCellZ = FloorDiv(worldZ - VeinRadius, VeinCellSize);
        var maxCellZ = FloorDiv(worldZ + VeinRadius, VeinCellSize);

        for (var cx = minCellX; cx <= maxCellX; cx++)
        {
            for (var cy = minCellY; cy <= maxCellY; cy++)
            {
                for (var cz = minCellZ; cz <= maxCellZ; cz++)
                {
                    var ore = TryVeinAtCell(cx, cy, cz, worldX, worldY, worldZ, seed, deep);
                    if (ore != 0)
                        return ore;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Builds the fixed cell grid needed by one 16x16 column. The stack-owned caller reuses these
    /// immutable cell values for every voxel, eliminating repeated Hash3/origin work.
    /// </summary>
    internal static int FillColumnCells(
        int baseX,
        int baseZ,
        int seed,
        Span<OreCell> cells,
        out int minCellX,
        out int minCellY,
        out int minCellZ,
        out int widthX,
        out int widthY,
        out int widthZ)
    {
        minCellX = FloorDiv(baseX - VeinRadius, VeinCellSize);
        var maxCellX = FloorDiv(baseX + 15 + VeinRadius, VeinCellSize);
        minCellY = FloorDiv(ColumnMinOreY - VeinRadius, VeinCellSize);
        var maxCellY = FloorDiv(ColumnMaxOreY + VeinRadius, VeinCellSize);
        minCellZ = FloorDiv(baseZ - VeinRadius, VeinCellSize);
        var maxCellZ = FloorDiv(baseZ + 15 + VeinRadius, VeinCellSize);
        widthX = maxCellX - minCellX + 1;
        widthY = maxCellY - minCellY + 1;
        widthZ = maxCellZ - minCellZ + 1;
        var count = checked(widthX * widthY * widthZ);
        if (count > cells.Length)
            throw new ArgumentException("Ore cell buffer is too small for a column.", nameof(cells));

        var index = 0;
        for (var cx = minCellX; cx <= maxCellX; cx++)
        for (var cy = minCellY; cy <= maxCellY; cy++)
        for (var cz = minCellZ; cz <= maxCellZ; cz++)
            cells[index++] = OreCell.Create(cx, cy, cz, seed);

        return count;
    }

    /// <summary>
    /// Takes <paramref name="deep"/> directly rather than a host block id — unlike the per-point
    /// overload above, this one is only ever called from the full-column generation hot path, which
    /// already knows deep/shallow from its own worldY branch. Re-deriving it here via
    /// <see cref="Blocks.Stone"/>/<see cref="Blocks.Deepslate"/> property comparisons measured as a
    /// real, avoidable cost per voxel (profiling — each access re-checks <c>EnsureLoaded()</c>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TryReplaceHost(
        bool deep,
        int worldX,
        int worldY,
        int worldZ,
        ReadOnlySpan<OreCell> cells,
        int minCellX,
        int minCellY,
        int minCellZ,
        int widthX,
        int widthY,
        int widthZ)
    {
        var minX = FloorDiv(worldX - VeinRadius, VeinCellSize);
        var maxX = FloorDiv(worldX + VeinRadius, VeinCellSize);
        var minY = FloorDiv(worldY - VeinRadius, VeinCellSize);
        var maxY = FloorDiv(worldY + VeinRadius, VeinCellSize);
        var minZ = FloorDiv(worldZ - VeinRadius, VeinCellSize);
        var maxZ = FloorDiv(worldZ + VeinRadius, VeinCellSize);

        for (var cx = minX; cx <= maxX; cx++)
        for (var cy = minY; cy <= maxY; cy++)
        for (var cz = minZ; cz <= maxZ; cz++)
        {
            var index = ((cx - minCellX) * widthY + (cy - minCellY)) * widthZ + (cz - minCellZ);
            var ore = TryVeinAtCell(in cells[index], worldX, worldY, worldZ, deep);
            if (ore != 0)
                return ore;
        }

        return 0;
    }

    private static int TryVeinAtCell(
        int cellX,
        int cellY,
        int cellZ,
        int worldX,
        int worldY,
        int worldZ,
        int seed,
        bool deepslateHost)
    {
        var h = Hash3(cellX, cellY, cellZ, seed ^ OreSalt);
        if ((h % 7) != 0)
            return 0;

        var originX = cellX * VeinCellSize + (int)(h % (uint)VeinCellSize);
        var originY = cellY * VeinCellSize + (int)((h >> 4) % (uint)VeinCellSize);
        var originZ = cellZ * VeinCellSize + (int)((h >> 8) % (uint)VeinCellSize);

        if (Chebyshev(worldX, worldY, worldZ, originX, originY, originZ) > VeinRadius)
            return 0;

        return PickOreBlock(worldY, h, deepslateHost);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int TryVeinAtCell(in OreCell cell, int worldX, int worldY, int worldZ, bool deepslateHost)
    {
        if ((cell.Hash % 7) != 0)
            return 0;
        if (Chebyshev(worldX, worldY, worldZ, cell.OriginX, cell.OriginY, cell.OriginZ) > VeinRadius)
            return 0;
        return PickOreBlock(worldY, cell.Hash, deepslateHost);
    }

    /// <summary>
    /// Phase XXIV cross-reference finding: several vertical bands were checked against an
    /// independent reference server's vanilla-faithful generator and corrected. Diamond was far too
    /// shallow (y≤16 let it appear near the surface; real diamond skews deep, y≤-4). Coal had no
    /// lower bound at all (any y, including diamond depth) — a real gameable bug, not just
    /// imprecision, since it diluted vertical strata distinctiveness and let players find coal mixed
    /// into the deepest veins. Iron was missing vanilla's second, shallower band entirely (real iron
    /// generates both deep AND near hilltops); added a modest shallow band since Zenith's terrain
    /// rarely exceeds ~y120 anyway. Copper's upper bound was extended to match. Lapis and tree height
    /// were confirmed already correct and left unchanged.
    /// </summary>
    private static int PickOreBlock(int worldY, uint h, bool deepslate)
    {
        var r = (int)(h >> 12);
        if (worldY <= -4 && r % 120 == 0)
            return deepslate ? Blocks.DeepslateDiamondOre : Blocks.DiamondOre;
        if (worldY is >= -32 and <= 32 && r % 80 == 0)
            return deepslate ? Blocks.DeepslateLapisOre : Blocks.LapisOre;
        if (worldY <= 16 && r % 50 == 0)
            return deepslate ? Blocks.DeepslateRedstoneOre : Blocks.RedstoneOre;
        if (worldY <= 32 && r % 45 == 0)
            return deepslate ? Blocks.DeepslateGoldOre : Blocks.GoldOre;
        if ((worldY <= 64 || worldY is >= 80 and <= 120) && r % 25 == 0)
            return deepslate ? Blocks.DeepslateIronOre : Blocks.IronOre;
        if (worldY <= 112 && r % 20 == 0)
            return deepslate ? Blocks.DeepslateCopperOre : Blocks.CopperOre;
        if (worldY >= 0 && r % 12 == 0)
            return deepslate ? Blocks.DeepslateCoalOre : Blocks.CoalOre;

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Chebyshev(int ax, int ay, int az, int bx, int by, int bz)
    {
        var dx = Math.Abs(ax - bx);
        var dy = Math.Abs(ay - by);
        var dz = Math.Abs(az - bz);
        return Math.Max(dx, Math.Max(dy, dz));
    }

    // Called 6x per TryReplaceHost invocation (min/max cell per axis) — a hot per-voxel path during
    // full-column generation. AggressiveInlining lets RyuJIT constant-fold `divisor` at call sites
    // that pass the VeinCellSize literal, turning the division into a shift instead of an idiv.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int FloorDiv(int value, int divisor)
    {
        if (value >= 0) return value / divisor;
        return (value - (divisor - 1)) / divisor;
    }

    internal static uint Hash3(int x, int y, int z, int seed)
    {
        unchecked
        {
            var h = (uint)seed;
            h ^= (uint)x * 374761393u;
            h ^= (uint)y * 668265263u;
            h ^= (uint)z * 2147483647u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h;
        }
    }
}

readonly struct OreCell
{
    public readonly uint Hash;
    public readonly int OriginX;
    public readonly int OriginY;
    public readonly int OriginZ;

    private OreCell(uint hash, int originX, int originY, int originZ)
    {
        Hash = hash;
        OriginX = originX;
        OriginY = originY;
        OriginZ = originZ;
    }

    public static OreCell Create(int cellX, int cellY, int cellZ, int seed)
    {
        var hash = OverworldOrePlacer.Hash3(cellX, cellY, cellZ, seed ^ OverworldOrePlacer.OreSalt);
        return new OreCell(
            hash,
            cellX * OverworldOrePlacer.VeinCellSize + (int)(hash % (uint)OverworldOrePlacer.VeinCellSize),
            cellY * OverworldOrePlacer.VeinCellSize + (int)((hash >> 4) % (uint)OverworldOrePlacer.VeinCellSize),
            cellZ * OverworldOrePlacer.VeinCellSize + (int)((hash >> 8) % (uint)OverworldOrePlacer.VeinCellSize));
    }
}
