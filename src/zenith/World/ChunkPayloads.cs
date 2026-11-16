using System.Buffers;

namespace Zenith.World;

/// <summary>Generates the sections of an overworld column from a terrain sampler (ADR §69: single pass, pooled scratch).</summary>
static class ChunkPayloads
{
    private const int OverworldMinSubChunkIndex = -4;
    private const int OverworldMaxSubChunkIndex = 19;
    private const int PlainsBiomeId = 1;
    private const int SectionVolume = Section.Volume;
    /// <summary>Trunk ≤6 + canopy + margin above max surface.</summary>
    private const int NoiseFeatureHeadroom = 8;

    /// <summary>
    /// Flat: classic stone/grass column — delegates to <see cref="BuildOverworldColumn"/>.
    /// </summary>
    public static TerrainColumn BuildFlatOverworld()
        => BuildOverworldColumn(
            0,
            0,
            (x, y, z) => OverworldTerrainSampler.SampleBlock(x, y, z, Blocks.FlatGrassY),
            PlainsBiomeId,
            maxWorldY: Blocks.FlatGrassY);

    /// <summary>Noise overworld — staged surfaces, feature plan, cave context and single-pass sections (ADR §69).</summary>
    public static TerrainColumn BuildNoiseOverworldColumn(
        int chunkX,
        int chunkZ,
        int seed,
        OverworldCaveContext caves,
        WorldGenerationDiagnostics? generationDiagnostics = null)
    {
        Span<int> surfaces = stackalloc int[256];
        Span<OverworldBiomeKind> biomes = stackalloc OverworldBiomeKind[256];
        int maxSurface;
        var surfaceScope = generationDiagnostics is null ? default : generationDiagnostics.BeginSurface();
        using (surfaceScope)
            OverworldTerrainSampler.FillColumnSurfaces(chunkX, chunkZ, seed, surfaces, biomes, out maxSurface);

        // The cave context is built independently of terrain surfaces so point queries remain
        // cheap to construct. Full-column generation has the complete surface field now; turn
        // the segment geometry into an O(1) bit lookup before entering the voxel loop.
        var caveMaskScope = generationDiagnostics is null ? default : generationDiagnostics.BeginCaves();
        using (caveMaskScope)
            caves.PrepareColumnMask(surfaces);

        OverworldTerrainSampler.FeaturePlacementPlan features;
        int featureMaxY;
        var featureScope = generationDiagnostics is null ? default : generationDiagnostics.BeginFeatures();
        using (featureScope)
        {
            features = OverworldTerrainSampler.FeaturePlacementPlan.Build(chunkX, chunkZ, seed);
            featureMaxY = OverworldTerrainSampler.MaxTreeCanopyYAffectingChunk(chunkX, chunkZ, seed, features);
        }

        var maxWorldY = Math.Max(maxSurface + NoiseFeatureHeadroom, OverworldTerrainSampler.SeaLevel);
        if (featureMaxY > maxWorldY)
            maxWorldY = featureMaxY;
        var biomeId = OverworldBiomeSampler.NetworkId(biomes[(8 << 4) | 8]);
        var maxSubChunk = Math.Clamp(
            SectionIndex(maxWorldY),
            0,
            OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex);

        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        Span<OreCell> oreCells = stackalloc OreCell[OverworldOrePlacer.MaxColumnCellCount];
        var oreCellCount = OverworldOrePlacer.FillColumnCells(
            baseX, baseZ, seed, oreCells,
            out var minOreCellX, out var minOreCellY, out var minOreCellZ,
            out var oreWidthX, out var oreWidthY, out var oreWidthZ);
        var oreView = oreCells[..oreCellCount];
        var ids = ArrayPool<int>.Shared.Rent(SectionVolume);
        var payloadScope = generationDiagnostics is null ? default : generationDiagnostics.BeginPayload();
        try
        {
            using (payloadScope)
            {
                var sections = new Section?[WorldColumn.SectionCount];
                for (var section = 0; section <= maxSubChunk; section++)
                {
                    var worldYBase = OverworldMinSubChunkIndex * 16 + section * 16;
                    var samplingScope = generationDiagnostics is null ? default : generationDiagnostics.BeginSampling();
                    using (samplingScope)
                    {
                        for (var lx = 0; lx < 16; lx++)
                        {
                            for (var lz = 0; lz < 16; lz++)
                            {
                                var meta = (lx << 4) | lz;
                                var wx = baseX + lx;
                                var wz = baseZ + lz;
                                var surface = surfaces[meta];
                                var biome = biomes[meta];
                                for (var localY = 0; localY < 16; localY++)
                                {
                                    ids[BlockIndex(lx, localY, lz)] = OverworldTerrainSampler.SampleNoiseBlockAtSurfaceWithOre(
                                        wx, worldYBase + localY, wz, seed, surface, caves, biome, features,
                                        oreView, minOreCellX, minOreCellY, minOreCellZ,
                                        oreWidthX, oreWidthY, oreWidthZ);
                                }
                            }
                        }
                    }

                    var encodeScope = generationDiagnostics is null ? default : generationDiagnostics.BeginEncode();
                    using (encodeScope)
                        sections[section] = Section.FromIds(ids.AsSpan(0, SectionVolume));
                }

                return new TerrainColumn(sections, biomeId);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ids, clearArray: false);
        }
    }

    /// <summary>
    /// Builds paletted subchunks for one column from a world-space block sampler.
    /// SubChunkCount = highest non-air section index + 1 (Bedrock implicit air above).
    /// </summary>
    public static TerrainColumn BuildOverworldColumn(
        int chunkX,
        int chunkZ,
        Func<int, int, int, int> blockAtWorld,
        int biomeNetworkId = PlainsBiomeId,
        int? maxWorldY = null)
    {
        var baseX = chunkX << 4;
        var baseZ = chunkZ << 4;
        var boundY = maxWorldY ?? (OverworldMaxSubChunkIndex * 16 + 15);
        var maxSubChunk = Math.Clamp(
            SectionIndex(boundY),
            0,
            OverworldMaxSubChunkIndex - OverworldMinSubChunkIndex);

        var ids = ArrayPool<int>.Shared.Rent(SectionVolume);
        try
        {
            var sections = new Section?[WorldColumn.SectionCount];
            for (var section = 0; section <= maxSubChunk; section++)
            {
                var worldYBase = OverworldMinSubChunkIndex * 16 + section * 16;
                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        for (var localY = 0; localY < 16; localY++)
                            ids[BlockIndex(x, localY, z)] = blockAtWorld(baseX + x, worldYBase + localY, baseZ + z);
                    }
                }

                sections[section] = Section.FromIds(ids.AsSpan(0, SectionVolume));
            }

            return new TerrainColumn(sections, biomeNetworkId);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ids, clearArray: false);
        }
    }

    private static int SectionIndex(int worldY)
        => (worldY - OverworldMinSubChunkIndex * 16) >> 4;

    private static int BlockIndex(int x, int y, int z) => (x << 8) | (z << 4) | y;
}
