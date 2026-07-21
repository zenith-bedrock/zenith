namespace Zenith.World;

/// <summary>Deterministic overworld — biomes, hills, water, caves, trees, ruins, ore (ADR §63–§69).</summary>
sealed class NoiseTerrainProvider : ITerrainProvider
{
    private readonly int _seed;

    public NoiseTerrainProvider(int seed) => _seed = seed;

    public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
    {
        var caves = OverworldCaveContext.ForColumn(chunkX, chunkZ, _seed);
        var (subChunkCount, payload) = ChunkPayloads.BuildNoiseOverworldColumn(chunkX, chunkZ, _seed, caves);
        return new TerrainColumn(subChunkCount, payload);
    }

    public int SampleBaseBlock(int x, int y, int z)
        => OverworldTerrainSampler.SampleNoiseBlock(x, y, z, _seed);

    public int SampleSpawnFeetY(int x, int z)
        => OverworldTerrainSampler.SampleSpawnFeetY(x, z, _seed);

    public SpawnBiome SampleSpawnBiome(int x, int z)
        => OverworldBiomeSampler.SampleSpawnBiome(x, z, _seed);
}
