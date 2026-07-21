namespace Zenith.World;

/// <summary>
/// Shared overworld height + block rules for terrain providers (ADR §63 / §64).
/// <see cref="SampleNoiseBlock"/> must stay consistent with <see cref="ChunkPayloads.BuildOverworldColumn"/>.
/// Flat mode keeps classic Y≈-61 via <see cref="SampleBlock"/> with constant surface.
/// </summary>
static class OverworldTerrainSampler
{
    /// <summary>Legacy flat hill variation (unused by §64 noise; kept for API clarity).</summary>
    public const int MaxSurfaceRise = 6;

    public const int NoiseDirtDepth = 3;

    /// <summary>Vanilla-ish sea level — valleys below this fill with water.</summary>
    public const int SeaLevel = 62;

    /// <summary>Noise mean surface (Bedrock overworld band, not classic flat).</summary>
    public const int NoiseBaseSurfaceY = 64;

    /// <summary>Coarse height swing around <see cref="NoiseBaseSurfaceY"/>.</summary>
    public const int NoiseHillAmplitude = 24;

    public const int TreeCellSize = 10;
    public const int RuinCellSize = 40;

    private const int TreeSalt = unchecked((int)0x7EE7E77Eu);
    private const int RuinSalt = unchecked((int)0x5015015u);
    private const int CaveSalt = unchecked((int)0xCAFEBABEu);
    private const int TunnelSalt = unchecked((int)0x71177117u);
    private const int FineSalt = unchecked((int)0xA5A55A5Au);

    public static int SurfaceY(int worldX, int worldZ, int seed)
    {
        // Coarse hills + fine jitter — deterministic, no floats.
        var coarse = (int)(Hash(worldX >> 2, worldZ >> 2, seed) % (uint)(NoiseHillAmplitude * 2 + 1))
                     - NoiseHillAmplitude;
        var fine = (int)(Hash(worldX, worldZ, seed ^ FineSalt) % 5);
        var y = NoiseBaseSurfaceY + coarse + fine;
        return Math.Clamp(y, Blocks.FlatMinY + 12, 120);
    }

    /// <summary>Domain feet Y standing on dry surface or water top.</summary>
    public static int SpawnFeetY(int surfaceY) => Math.Max(surfaceY, SeaLevel) + 1;

    /// <summary>
    /// Clear air column above terrain/features at (x,z) for join/respawn.
    /// </summary>
    public static int SampleSpawnFeetY(int worldX, int worldZ, int seed)
    {
        var feet = SpawnFeetY(SurfaceY(worldX, worldZ, seed));
        for (var i = 0; i < 24; i++)
        {
            if (SampleNoiseBlock(worldX, feet, worldZ, seed) == Blocks.Air
                && SampleNoiseBlock(worldX, feet + 1, worldZ, seed) == Blocks.Air)
                return feet;
            feet++;
        }

        return feet;
    }

    /// <summary>Flat / constant-surface sample (no trees/caves/water).</summary>
    public static int SampleBlock(int worldX, int worldY, int worldZ, int surfaceY, int dirtDepth = 0)
    {
        _ = worldX;
        _ = worldZ;
        if (worldY > surfaceY) return Blocks.Air;
        if (worldY == surfaceY) return Blocks.GrassBlock;
        if (dirtDepth > 0
            && worldY >= surfaceY - dirtDepth
            && worldY < surfaceY
            && worldY >= Blocks.FlatMinY)
            return Blocks.Dirt;
        if (worldY >= Blocks.FlatMinY) return Blocks.Stone;
        return Blocks.Air;
    }

    /// <summary>§64 noise column sample: water, caves, trees, ruins, deepslate, bedrock.</summary>
    public static int SampleNoiseBlock(int worldX, int worldY, int worldZ, int seed)
    {
        if (worldY < Blocks.FlatMinY || worldY > 320) return Blocks.Air;

        var feature = SampleFeature(worldX, worldY, worldZ, seed);
        if (feature != Blocks.Air) return feature;

        var surface = SurfaceY(worldX, worldZ, seed);
        if (worldY > surface && worldY <= SeaLevel) return Blocks.Water;
        if (worldY > surface) return Blocks.Air;
        if (worldY == Blocks.FlatMinY) return Blocks.Bedrock;

        if (IsCave(worldX, worldY, worldZ, seed, surface))
            return Blocks.Air;

        if (worldY == surface) return Blocks.GrassBlock;
        if (worldY >= surface - NoiseDirtDepth && worldY < surface)
            return Blocks.Dirt;
        if (worldY < 0) return Blocks.Deepslate;
        return Blocks.Stone;
    }

    private static int SampleFeature(int x, int y, int z, int seed)
    {
        var tree = SampleTree(x, y, z, seed);
        if (tree != Blocks.Air) return tree;
        return SampleRuin(x, y, z, seed);
    }

    private static int SampleTree(int x, int y, int z, int seed)
    {
        var cellX = FloorDiv(x, TreeCellSize);
        var cellZ = FloorDiv(z, TreeCellSize);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!TryTreeAnchor(cellX + dx, cellZ + dz, seed, out var tx, out var tz, out var trunkH))
                    continue;

                var surface = SurfaceY(tx, tz, seed);
                if (surface < SeaLevel) continue;

                // Trunk
                if (x == tx && z == tz && y > surface && y <= surface + trunkH)
                    return Blocks.OakLog;

                // Canopy: 5×5 mid layers, 3×3 top, corners skipped on bottom.
                var top = surface + trunkH;
                if (y < top - 2 || y > top + 1) continue;
                var lx = Math.Abs(x - tx);
                var lz = Math.Abs(z - tz);
                if (y == top + 1)
                {
                    if (lx <= 1 && lz <= 1) return Blocks.OakLeaves;
                    continue;
                }

                if (lx > 2 || lz > 2) continue;
                if (y == top - 2 && lx == 2 && lz == 2) continue;
                if (x == tx && z == tz && y <= top) continue; // trunk owns stem cells
                return Blocks.OakLeaves;
            }
        }

        return Blocks.Air;
    }

    private static bool TryTreeAnchor(int cellX, int cellZ, int seed, out int tx, out int tz, out int trunkH)
    {
        var h = Hash(cellX, cellZ, seed ^ TreeSalt);
        // ~22% of cells get a tree.
        if ((h % 9) > 1)
        {
            tx = tz = trunkH = 0;
            return false;
        }

        tx = cellX * TreeCellSize + (int)(h % (uint)TreeCellSize);
        tz = cellZ * TreeCellSize + (int)((h >> 8) % (uint)TreeCellSize);
        trunkH = 4 + (int)((h >> 16) % 3);
        return true;
    }

    private static int SampleRuin(int x, int y, int z, int seed)
    {
        var cellX = FloorDiv(x, RuinCellSize);
        var cellZ = FloorDiv(z, RuinCellSize);
        var h = Hash(cellX, cellZ, seed ^ RuinSalt);
        if ((h % 11) != 0) return Blocks.Air;

        var ox = cellX * RuinCellSize + 8 + (int)(h % 16);
        var oz = cellZ * RuinCellSize + 8 + (int)((h >> 8) % 16);
        var surface = SurfaceY(ox, oz, seed);
        if (surface < SeaLevel) return Blocks.Air;

        var lx = x - ox;
        var lz = z - oz;
        if (lx is < 0 or > 4 || lz is < 0 or > 4) return Blocks.Air;

        // 5×5 cobble floor on surface; corner pillars + plank ring.
        if (y == surface)
            return Blocks.Cobblestone;
        if (y == surface + 1
            && (lx, lz) is (0, 0) or (0, 4) or (4, 0) or (4, 4))
            return Blocks.Cobblestone;
        if (y == surface + 1 && lx is >= 1 and <= 3 && lz is >= 1 and <= 3
            && (lx == 1 || lx == 3 || lz == 1 || lz == 3))
            return Blocks.OakPlanks;

        return Blocks.Air;
    }

    private static bool IsCave(int x, int y, int z, int seed, int surface)
    {
        if (y <= Blocks.FlatMinY + 1 || y >= surface - 4) return false;
        var chamber = Hash3(x >> 2, y >> 2, z >> 2, seed ^ CaveSalt);
        if ((chamber & 0xFF) > 22) return false;
        var tunnel = Hash3(x, y, z, seed ^ TunnelSalt);
        return (tunnel & 0xFF) < 48;
    }

    private static int FloorDiv(int value, int divisor)
    {
        if (value >= 0) return value / divisor;
        return (value - (divisor - 1)) / divisor;
    }

    private static uint Hash(int x, int z, int seed)
    {
        unchecked
        {
            var h = (uint)seed;
            h ^= (uint)x * 374761393u;
            h ^= (uint)z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h;
        }
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
