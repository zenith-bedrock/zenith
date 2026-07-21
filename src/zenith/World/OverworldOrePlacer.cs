namespace Zenith.World;

/// <summary>
/// Deterministic ore vein clusters for noise overworld (ADR §66).
/// Replaces stone/deepslate hosts only — never grass, dirt, air, or carved cells.
/// </summary>
static class OverworldOrePlacer
{
    internal const int VeinCellSize = 8;
    internal const int VeinRadius = 2;

    private const int OreSalt = unchecked((int)0x0EE0E001u);

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

    private static int PickOreBlock(int worldY, uint h, bool deepslate)
    {
        var r = (int)(h >> 12);
        if (worldY <= 16 && r % 120 == 0)
            return deepslate ? Blocks.DeepslateDiamondOre : Blocks.DiamondOre;
        if (worldY is >= -32 and <= 32 && r % 80 == 0)
            return deepslate ? Blocks.DeepslateLapisOre : Blocks.LapisOre;
        if (worldY <= 16 && r % 50 == 0)
            return deepslate ? Blocks.DeepslateRedstoneOre : Blocks.RedstoneOre;
        if (worldY <= 32 && r % 45 == 0)
            return deepslate ? Blocks.DeepslateGoldOre : Blocks.GoldOre;
        if (worldY <= 64 && r % 25 == 0)
            return deepslate ? Blocks.DeepslateIronOre : Blocks.IronOre;
        if (worldY <= 96 && r % 20 == 0)
            return deepslate ? Blocks.DeepslateCopperOre : Blocks.CopperOre;
        if (r % 12 == 0)
            return deepslate ? Blocks.DeepslateCoalOre : Blocks.CoalOre;

        return 0;
    }

    private static int Chebyshev(int ax, int ay, int az, int bx, int by, int bz)
    {
        var dx = Math.Abs(ax - bx);
        var dy = Math.Abs(ay - by);
        var dz = Math.Abs(az - bz);
        return Math.Max(dx, Math.Max(dy, dz));
    }

    private static int FloorDiv(int value, int divisor)
    {
        if (value >= 0) return value / divisor;
        return (value - (divisor - 1)) / divisor;
    }

    private static uint Hash3(int x, int y, int z, int seed)
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
