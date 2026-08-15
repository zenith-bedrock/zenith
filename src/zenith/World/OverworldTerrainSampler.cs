using System.Runtime.CompilerServices;

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
        FillSurfaceAt(worldX, worldZ, seed, OverworldNoiseFields.For(seed), out var y, out _);
        return y;
    }

    private static int SurfaceY(int worldX, int worldZ, int seed, OverworldNoiseFields.Fields fields)
    {
        FillSurfaceAt(worldX, worldZ, seed, fields, out var y, out _);
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
        var fields = OverworldNoiseFields.For(seed);
        maxSurfaceY = int.MinValue;
        for (var lx = 0; lx < 16; lx++)
        {
            for (var lz = 0; lz < 16; lz++)
            {
                var i = (lx << 4) | lz;
                FillSurfaceAt(baseX + lx, baseZ + lz, seed, fields, out var y, out var biome);
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
    internal static int MaxTreeCanopyYAffectingChunk(
        int chunkX,
        int chunkZ,
        int seed,
        FeaturePlacementPlan? features = null)
    {
        if (features is not null)
            return features.MaxYForChunk;

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

    private static void FillSurfaceAt(
        int worldX,
        int worldZ,
        int seed,
        OverworldNoiseFields.Fields fields,
        out int y,
        out OverworldBiomeKind biome)
    {
        OverworldBiomeSampler.SampleClimate(fields, worldX, worldZ, out var temperature, out var rainfall);
        biome = OverworldBiomeSampler.Lookup(temperature, rainfall);
        var n = fields.Height.GetNoise(worldX, worldZ);
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

    /// <summary>Column fill path — surface/biome and deterministic feature plan are already prepared (ADR §69).</summary>
    internal static int SampleNoiseBlockAtSurface(
        int worldX,
        int worldY,
        int worldZ,
        int seed,
        int surface,
        OverworldCaveContext? caves,
        OverworldBiomeKind? biome = null,
        FeaturePlacementPlan? features = null)
        => SampleNoiseBlockAtSurfaceCore(
            worldX, worldY, worldZ, seed, surface, caves, biome, features,
            default, 0, 0, 0, 0, 0, 0);

    internal static int SampleNoiseBlockAtSurfaceWithOre(
        int worldX,
        int worldY,
        int worldZ,
        int seed,
        int surface,
        OverworldCaveContext? caves,
        OverworldBiomeKind biome,
        FeaturePlacementPlan features,
        ReadOnlySpan<OreCell> oreCells,
        int minCellX,
        int minCellY,
        int minCellZ,
        int widthX,
        int widthY,
        int widthZ)
        => SampleNoiseBlockAtSurfaceCore(
            worldX, worldY, worldZ, seed, surface, caves, biome, features,
            oreCells, minCellX, minCellY, minCellZ, widthX, widthY, widthZ);

    private static int SampleNoiseBlockAtSurfaceCore(
        int worldX,
        int worldY,
        int worldZ,
        int seed,
        int surface,
        OverworldCaveContext? caves,
        OverworldBiomeKind? biome,
        FeaturePlacementPlan? features,
        ReadOnlySpan<OreCell> oreCells,
        int minCellX,
        int minCellY,
        int minCellZ,
        int widthX,
        int widthY,
        int widthZ)
    {
        if (worldY < Blocks.FlatMinY || worldY > 320) return Blocks.Air;

        if (worldY > surface)
        {
            if (worldY <= SeaLevel) return Blocks.Water;
            if (features is not null)
                return features.TryGet(worldX, worldY, worldZ, out var aboveBlock) ? aboveBlock : Blocks.Air;

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
            var ore = TryOre(
                Blocks.Deepslate, worldX, worldY, worldZ, seed, oreCells,
                minCellX, minCellY, minCellZ, widthX, widthY, widthZ);
            return ore != 0 ? ore : Blocks.Deepslate;
        }

        int feature;
        if (features is not null)
            feature = features.TryGet(worldX, worldY, worldZ, out var featureBlock) ? featureBlock : Blocks.Air;
        else
            feature = SampleFeature(worldX, worldY, worldZ, seed);
        if (feature != Blocks.Air) return feature;

        var stoneOre = TryOre(
            Blocks.Stone, worldX, worldY, worldZ, seed, oreCells,
            minCellX, minCellY, minCellZ, widthX, widthY, widthZ);
        return stoneOre != 0 ? stoneOre : Blocks.Stone;
    }

    /// <summary>
    /// Fast per-column cell-grid ore lookup, guarded by the grid's own Y range. The grid built by
    /// <see cref="OverworldOrePlacer.FillColumnCells"/> only covers
    /// [<see cref="OverworldOrePlacer.ColumnMinOreY"/>, <see cref="OverworldOrePlacer.ColumnMaxOreY"/>]
    /// — a <paramref name="worldY"/> outside that band (current terrain never reaches it, but nothing
    /// enforces that as an invariant) falls back to the always-safe per-point overload instead of
    /// indexing outside the precomputed cells.
    /// </summary>
    private static int TryOre(
        int hostBlock, int worldX, int worldY, int worldZ, int seed,
        ReadOnlySpan<OreCell> oreCells,
        int minCellX, int minCellY, int minCellZ, int widthX, int widthY, int widthZ)
    {
        if (oreCells.IsEmpty
            || worldY < OverworldOrePlacer.ColumnMinOreY
            || worldY > OverworldOrePlacer.ColumnMaxOreY)
            return OverworldOrePlacer.TryReplaceHost(hostBlock, worldX, worldY, worldZ, seed);

        // The fast overload takes `deep` directly (not `hostBlock`) — both call sites here already
        // know it from their own worldY branch, and profiling showed the Stone/Deepslate property
        // getters (each does a per-access EnsureLoaded() check) as a measurable share of this
        // per-voxel hot path when re-derived redundantly inside the fast overload itself.
        var deep = hostBlock == Blocks.Deepslate;
        return OverworldOrePlacer.TryReplaceHost(
            deep, worldX, worldY, worldZ, oreCells,
            minCellX, minCellY, minCellZ, widthX, widthY, widthZ);
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
                if (!TryTreeAnchor(cellX + dx, cellZ + dz, seed, out var atx, out var atz, out var trunkHeight))
                    continue;

                var surface = SurfaceY(atx, atz, seed);
                if (surface < SeaLevel) continue;

                if (x == atx && z == atz && y > surface && y <= surface + trunkHeight)
                    return Blocks.OakLog;

                var top = surface + trunkHeight;
                if (y < top - 2 || y > top + 1) continue;
                var lx = Math.Abs(x - atx);
                var lz = Math.Abs(z - atz);
                if (y == top + 1)
                {
                    if (lx <= 1 && lz <= 1) return Blocks.OakLeaves;
                    continue;
                }

                if (lx > 2 || lz > 2) continue;
                if (y == top - 2 && lx == 2 && lz == 2) continue;
                if (x == atx && z == atz && y <= top) continue;
                return Blocks.OakLeaves;
            }
        }

        return Blocks.Air;
    }

    private static bool TryTreeAnchor(int cellX, int cellZ, int seed, out int tx, out int tz, out int trunkH)
        => TryTreeAnchor(cellX, cellZ, seed, OverworldNoiseFields.For(seed), out tx, out tz, out trunkH);

    private static bool TryTreeAnchor(
        int cellX,
        int cellZ,
        int seed,
        OverworldNoiseFields.Fields fields,
        out int tx,
        out int tz,
        out int trunkH)
    {
        var h = Hash(cellX, cellZ, seed ^ TreeSalt);
        tx = cellX * TreeCellSize + (int)(h % (uint)TreeCellSize);
        tz = cellZ * TreeCellSize + (int)((h >> 8) % (uint)TreeCellSize);
        OverworldBiomeSampler.SampleClimate(fields, tx, tz, out var temperature, out var rainfall);
        var biome = OverworldBiomeSampler.Lookup(temperature, rainfall);
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

    /// <summary>
    /// Deterministic feature placements for one chunk generation operation. Base terrain is sampled
    /// first, then this plan is applied as a read-only overlay while the chunk is encoded. It is
    /// deliberately scoped to one generation call: no global cache and no cross-chunk mutable state.
    /// </summary>
    internal sealed class FeaturePlacementPlan
    {
        private readonly int _chunkMinX;
        private readonly int _chunkMaxX;
        private readonly int _chunkMinZ;
        private readonly int _chunkMaxZ;
        private readonly int _seed;
        private readonly OverworldNoiseFields.Fields _fields;
        private readonly List<FeaturePlacement> _pending = new(256);
        private FeaturePlacement[] _placements = Array.Empty<FeaturePlacement>();
        private readonly int[] _columnOffsets = new int[257];

        public int MaxYForChunk { get; private set; } = int.MinValue;

        private FeaturePlacementPlan(int chunkX, int chunkZ, int seed)
        {
            _chunkMinX = chunkX << 4;
            _chunkMaxX = _chunkMinX + 15;
            _chunkMinZ = chunkZ << 4;
            _chunkMaxZ = _chunkMinZ + 15;
            _seed = seed;
            _fields = OverworldNoiseFields.For(seed);
            Build();
        }

        public static FeaturePlacementPlan Build(int chunkX, int chunkZ, int seed)
            => new(chunkX, chunkZ, seed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int x, int y, int z, out int block)
        {
            if (x < _chunkMinX || x > _chunkMaxX || z < _chunkMinZ || z > _chunkMaxZ)
            {
                block = 0;
                return false;
            }

            var column = ((x - _chunkMinX) << 4) | (z - _chunkMinZ);
            for (var index = _columnOffsets[column]; index < _columnOffsets[column + 1]; index++)
            {
                var placement = _placements[index];
                if (placement.Y != y) continue;
                block = placement.Block;
                return true;
            }

            block = 0;
            return false;
        }

        private void Build()
        {
            var minTreeCellX = FloorDiv(_chunkMinX, TreeCellSize) - 1;
            var maxTreeCellX = FloorDiv(_chunkMaxX, TreeCellSize) + 1;
            var minTreeCellZ = FloorDiv(_chunkMinZ, TreeCellSize) - 1;
            var maxTreeCellZ = FloorDiv(_chunkMaxZ, TreeCellSize) + 1;

            // Add trees in the same coordinate order as SampleTree's candidate traversal. This
            // preserves deterministic first-writer precedence if sparse structures overlap.
            for (var cellX = minTreeCellX; cellX <= maxTreeCellX; cellX++)
            for (var cellZ = minTreeCellZ; cellZ <= maxTreeCellZ; cellZ++)
            {
                if (!TryTreeAnchor(cellX, cellZ, _seed, _fields, out var x, out var z, out var trunkHeight))
                    continue;

                var surface = SurfaceY(x, z, _seed, _fields);
                if (surface < SeaLevel)
                    continue;

                var top = surface + trunkHeight;
                if (x >= _chunkMinX - TreeCanopyRadius && x <= _chunkMaxX + TreeCanopyRadius
                    && z >= _chunkMinZ - TreeCanopyRadius && z <= _chunkMaxZ + TreeCanopyRadius)
                    MaxYForChunk = Math.Max(MaxYForChunk, top + 1);

                for (var y = surface + 1; y <= top; y++)
                    AddTree(x, y, z, Blocks.OakLog);

                for (var y = top - 2; y <= top + 1; y++)
                for (var dx = -2; dx <= 2; dx++)
                for (var dz = -2; dz <= 2; dz++)
                {
                    var lx = Math.Abs(dx);
                    var lz = Math.Abs(dz);
                    if (y == top + 1 && (lx > 1 || lz > 1)) continue;
                    if (y == top - 2 && lx == 2 && lz == 2) continue;
                    if (y <= top && dx == 0 && dz == 0) continue;
                    AddTree(x + dx, y, z + dz, Blocks.OakLeaves);
                }
            }

            var minRuinCellX = FloorDiv(_chunkMinX, RuinCellSize);
            var maxRuinCellX = FloorDiv(_chunkMaxX, RuinCellSize);
            var minRuinCellZ = FloorDiv(_chunkMinZ, RuinCellSize);
            var maxRuinCellZ = FloorDiv(_chunkMaxZ, RuinCellSize);
            for (var cellX = minRuinCellX; cellX <= maxRuinCellX; cellX++)
            for (var cellZ = minRuinCellZ; cellZ <= maxRuinCellZ; cellZ++)
            {
                var h = Hash(cellX, cellZ, _seed ^ RuinSalt);
                if ((h % 11) != 0) continue;

                var x = cellX * RuinCellSize + 8 + (int)(h % 16);
                var z = cellZ * RuinCellSize + 8 + (int)((h >> 8) % 16);
                var surface = SurfaceY(x, z, _seed, _fields);
                if (surface < SeaLevel) continue;

                for (var dx = 0; dx <= 4; dx++)
                for (var dz = 0; dz <= 4; dz++)
                {
                    AddRuin(x + dx, surface, z + dz, Blocks.Cobblestone);
                    if ((dx, dz) is (0, 0) or (0, 4) or (4, 0) or (4, 4))
                        AddRuin(x + dx, surface + 1, z + dz, Blocks.Cobblestone);
                    else if (dx is >= 1 and <= 3 && dz is >= 1 and <= 3
                             && (dx == 1 || dx == 3 || dz == 1 || dz == 3))
                        AddRuin(x + dx, surface + 1, z + dz, Blocks.OakPlanks);
                }
            }

            FinalizePlacements();
        }

        private void AddTree(int x, int y, int z, int block)
            => AddPlacement(x, y, z, block);

        private void AddRuin(int x, int y, int z, int block)
            => AddPlacement(x, y, z, block);

        private void AddPlacement(int x, int y, int z, int block)
        {
            if (x < _chunkMinX || x > _chunkMaxX || z < _chunkMinZ || z > _chunkMaxZ)
                return;

            var localX = x - _chunkMinX;
            var localZ = z - _chunkMinZ;
            for (var index = 0; index < _pending.Count; index++)
            {
                var existing = _pending[index];
                if (existing.LocalX == localX && existing.LocalZ == localZ && existing.Y == y)
                    return;
            }

            _pending.Add(new FeaturePlacement((byte)localX, (byte)localZ, y, block));
        }

        private void FinalizePlacements()
        {
            _pending.Sort(static (left, right) =>
            {
                var leftColumn = (left.LocalX << 4) | left.LocalZ;
                var rightColumn = (right.LocalX << 4) | right.LocalZ;
                var columnOrder = leftColumn.CompareTo(rightColumn);
                return columnOrder != 0 ? columnOrder : left.Y.CompareTo(right.Y);
            });

            Array.Clear(_columnOffsets, 0, _columnOffsets.Length);
            foreach (var placement in _pending)
                _columnOffsets[((placement.LocalX << 4) | placement.LocalZ) + 1]++;

            for (var column = 1; column < _columnOffsets.Length; column++)
                _columnOffsets[column] += _columnOffsets[column - 1];

            _placements = _pending.ToArray();
        }

        private readonly struct FeaturePlacement
        {
            public readonly byte LocalX;
            public readonly byte LocalZ;
            public readonly int Y;
            public readonly int Block;

            public FeaturePlacement(byte localX, byte localZ, int y, int block)
            {
                LocalX = localX;
                LocalZ = localZ;
                Y = y;
                Block = block;
            }
        }

    }
}
