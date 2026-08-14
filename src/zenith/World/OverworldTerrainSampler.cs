namespace Zenith.World;

/// <summary>
/// Shared overworld height + block rules for terrain providers (ADR §63 / §64 / §71 / §72).
/// Height: FastNoiseLite OpenSimplex2 FBm + continuous climate bias. Features: trees/ruins/caves/ore.
/// <see cref="SampleNoiseBlock"/> must stay consistent with <see cref="ChunkPayloads.BuildNoiseOverworldColumn"/>.
/// Flat mode keeps classic Y≈-61 via <see cref="SampleBlock"/> with constant surface.
/// </summary>
static class OverworldTerrainSampler
{
    /// <summary>Legacy flat hill variation (unused by §64 noise; kept for API clarity).</summary>
    public const int MaxSurfaceRise = 6;

    public const int NoiseDirtDepth = 3;

    /// <summary>
    /// Vanilla sea level — valleys below this fill with water. Corrected from 62 to 63 (Phase XXIV
    /// cross-reference finding: confirmed against an independent reference server's vanilla-faithful
    /// generator constant).
    /// </summary>
    public const int SeaLevel = 63;

    /// <summary>Noise mean surface (Bedrock overworld band, not classic flat).</summary>
    public const int NoiseBaseSurfaceY = 64;

    /// <summary>Coarse height swing around <see cref="NoiseBaseSurfaceY"/> (noise × this).</summary>
    public const int NoiseHillAmplitude = 22;

    /// <summary>Max |ΔY| between adjacent surface samples (continuity contract — ADR §72).</summary>
    public const int MaxAdjacentSurfaceStep = 6;

    public const int TreeCellSize = 10;
    public const int RuinCellSize = 40;

    /// <summary>Canopy extends ±2 from trunk — used for cross-chunk maxWorldY.</summary>
    public const int TreeCanopyRadius = 2;

    private const int TreeSalt = unchecked((int)0x7EE7E77Eu);
    private const int RuinSalt = unchecked((int)0x5015015u);

    public static int SurfaceY(int worldX, int worldZ, int seed)
    {
        FillSurfaceAt(worldX, worldZ, seed, out var y, out _);
        return y;
    }

    /// <summary>Column grid: 16×16 surface Y + biome (index = lx * 16 + lz).</summary>
    public static void FillColumnSurfaces(
        int chunkX,
        int chunkZ,
        int seed,
        Span<int> surfaces256,
        Span<OverworldBiomeKind> biomes256,
        out int maxSurfaceY)
    {
        if (surfaces256.Length < 256 || biomes256.Length < 256)
            throw new ArgumentException("Need 256 slots for column surface grids.");

        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        maxSurfaceY = int.MinValue;
        for (var lx = 0; lx < 16; lx++)
        {
            for (var lz = 0; lz < 16; lz++)
            {
                var i = (lx << 4) | lz;
                FillSurfaceAt(baseX + lx, baseZ + lz, seed, out var y, out var biome);
                surfaces256[i] = y;
                biomes256[i] = biome;
                if (y > maxSurfaceY) maxSurfaceY = y;
            }
        }
    }

    /// <summary>
    /// Highest canopy Y from trees whose trunk can place leaves inside this chunk
    /// (canopy radius <see cref="TreeCanopyRadius"/>). <see cref="int.MinValue"/> if none.
    /// </summary>
    internal static int MaxTreeCanopyYAffectingChunk(int chunkX, int chunkZ, int seed)
    {
        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        var minX = baseX - TreeCanopyRadius;
        var maxX = baseX + 15 + TreeCanopyRadius;
        var minZ = baseZ - TreeCanopyRadius;
        var maxZ = baseZ + 15 + TreeCanopyRadius;
        var minCellX = FloorDiv(minX, TreeCellSize);
        var maxCellX = FloorDiv(maxX, TreeCellSize);
        var minCellZ = FloorDiv(minZ, TreeCellSize);
        var maxCellZ = FloorDiv(maxZ, TreeCellSize);

        var maxY = int.MinValue;
        for (var cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            for (var cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                if (!TryTreeAnchor(cellX, cellZ, seed, out var tx, out var tz, out var trunkH))
                    continue;
                if (tx < minX || tx > maxX || tz < minZ || tz > maxZ)
                    continue;

                var surface = SurfaceY(tx, tz, seed);
                if (surface < SeaLevel) continue;
                maxY = Math.Max(maxY, surface + trunkH + 1);
            }
        }

        return maxY;
    }

    private static void FillSurfaceAt(int worldX, int worldZ, int seed, out int y, out OverworldBiomeKind biome)
    {
        OverworldBiomeSampler.SampleClimate(worldX, worldZ, seed, out var temperature, out var rainfall);
        biome = OverworldBiomeSampler.Lookup(temperature, rainfall);
        var n = OverworldNoiseFields.For(seed).Height.GetNoise(worldX, worldZ);
        var bias = OverworldBiomeSampler.ContinuousHeightBias(temperature, rainfall);
        // No hard ocean Min — that created 1-block cliffs at biome edges (ADR §72).
        y = NoiseBaseSurfaceY + (int)Math.Round(n * NoiseHillAmplitude + bias);
        y = Math.Clamp(y, Blocks.FlatMinY + 12, 120);
    }

    /// <summary>Domain feet Y standing on dry surface or water top.</summary>
    public static int SpawnFeetY(int surfaceY) => Math.Max(surfaceY, SeaLevel) + 1;

    /// <summary>
    /// Clear air column above terrain/features at (x,z) for join/respawn. Phase XXIV cross-reference
    /// finding: previously only checked the feet/head cells were air, never that anything solid
    /// actually supported them — a real hole (confirmed against an independent reference server's
    /// spawn-safety check, which also requires the block underfoot to be non-passable). Scanning
    /// upward through e.g. tree leaves could land a candidate with clear air above but more air (or a
    /// carved void) below, dropping the player. Now also requires the cell directly underfoot to be
    /// non-air.
    /// </summary>
    public static int SampleSpawnFeetY(int worldX, int worldZ, int seed)
    {
        var feet = SpawnFeetY(SurfaceY(worldX, worldZ, seed));
        for (var i = 0; i < 24; i++)
        {
            if (SampleNoiseBlock(worldX, feet, worldZ, seed) == Blocks.Air
                && SampleNoiseBlock(worldX, feet + 1, worldZ, seed) == Blocks.Air
                && SampleNoiseBlock(worldX, feet - 1, worldZ, seed) != Blocks.Air)
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

    /// <summary>§64/§65/§66/§67 noise column sample: biomes, water, caves, ores, trees, ruins.</summary>
    public static int SampleNoiseBlock(int worldX, int worldY, int worldZ, int seed)
        => SampleNoiseBlock(worldX, worldY, worldZ, seed, caves: null);

    internal static int SampleNoiseBlock(
        int worldX,
        int worldY,
        int worldZ,
        int seed,
        OverworldCaveContext? caves)
    {
        if (worldY < Blocks.FlatMinY || worldY > 320) return Blocks.Air;

        var surface = SurfaceY(worldX, worldZ, seed);
        return SampleNoiseBlockAtSurface(worldX, worldY, worldZ, seed, surface, caves);
    }

    /// <summary>Column fill path — surface/biome already cached (ADR §69).</summary>
    internal static int SampleNoiseBlockAtSurface(
        int worldX,
        int worldY,
        int worldZ,
        int seed,
        int surface,
        OverworldCaveContext? caves,
        OverworldBiomeKind? biome = null)
    {
        if (worldY < Blocks.FlatMinY || worldY > 320) return Blocks.Air;

        if (worldY > surface)
        {
            if (worldY <= SeaLevel) return Blocks.Water;
            var above = SampleFeature(worldX, worldY, worldZ, seed);
            return above != Blocks.Air ? above : Blocks.Air;
        }
        if (worldY == Blocks.FlatMinY) return Blocks.Bedrock;

        var carved = caves?.IsCarved(worldX, worldY, worldZ, surface)
                     ?? OverworldCaveCarver.IsCarved(worldX, worldY, worldZ, seed, surface);
        if (carved) return Blocks.Air;

        var kind = biome ?? OverworldBiomeSampler.SampleKind(worldX, worldZ, seed);
        if (worldY == surface) return OverworldBiomeSampler.SurfaceBlock(kind);
        if (worldY >= surface - NoiseDirtDepth && worldY < surface)
            return OverworldBiomeSampler.SubsurfaceBlock(kind);
        if (worldY < 0)
        {
            var ore = OverworldOrePlacer.TryReplaceHost(Blocks.Deepslate, worldX, worldY, worldZ, seed);
            return ore != 0 ? ore : Blocks.Deepslate;
        }

        var feature = SampleFeature(worldX, worldY, worldZ, seed);
        if (feature != Blocks.Air) return feature;

        var stoneOre = OverworldOrePlacer.TryReplaceHost(Blocks.Stone, worldX, worldY, worldZ, seed);
        return stoneOre != 0 ? stoneOre : Blocks.Stone;
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

                if (x == tx && z == tz && y > surface && y <= surface + trunkH)
                    return Blocks.OakLog;

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
                if (x == tx && z == tz && y <= top) continue;
                return Blocks.OakLeaves;
            }
        }

        return Blocks.Air;
    }

    private static bool TryTreeAnchor(int cellX, int cellZ, int seed, out int tx, out int tz, out int trunkH)
    {
        var h = Hash(cellX, cellZ, seed ^ TreeSalt);
        tx = cellX * TreeCellSize + (int)(h % (uint)TreeCellSize);
        tz = cellZ * TreeCellSize + (int)((h >> 8) % (uint)TreeCellSize);
        var biome = OverworldBiomeSampler.SampleKind(tx, tz, seed);
        if (!OverworldBiomeSampler.AllowsTrees(biome)
            || !OverworldBiomeSampler.TryTreeRoll(h, biome))
        {
            trunkH = 0;
            return false;
        }

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
}
