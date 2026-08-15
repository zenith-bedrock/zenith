namespace Zenith.World;

/// <summary>Deterministic overworld — FastNoiseLite height/biomes, caves, trees, ruins, ore (ADR §63–§72).</summary>
sealed class NoiseTerrainProvider : ITerrainProvider
{
    private readonly int _seed;
    private readonly WorldGenerationDiagnostics? _generationDiagnostics;

    public NoiseTerrainProvider(int seed, WorldGenerationDiagnostics? generationDiagnostics = null)
    {
        _seed = seed;
        _generationDiagnostics = generationDiagnostics;
    }

    public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
    {
        OverworldCaveContext caves;
        var caveScope = _generationDiagnostics is null ? default : _generationDiagnostics.BeginCaves();
        using (caveScope)
            caves = OverworldCaveContext.ForColumn(chunkX, chunkZ, _seed);

        using (caves)
        {
            var (subChunkCount, payload) = ChunkPayloads.BuildNoiseOverworldColumn(
                chunkX, chunkZ, _seed, caves, _generationDiagnostics);
            return new TerrainColumn(subChunkCount, payload);
        }
    }

    public int SampleBaseBlock(int x, int y, int z)
        => OverworldTerrainSampler.SampleNoiseBlock(x, y, z, _seed);

    public int SampleSpawnFeetY(int x, int z)
        => OverworldTerrainSampler.SampleSpawnFeetY(x, z, _seed);

    public SpawnBiome SampleSpawnBiome(int x, int z)
        => OverworldBiomeSampler.SampleSpawnBiome(x, z, _seed);
}
