namespace Zenith.World;

/// <summary>Deterministic overworld — hills, water, caves, trees, ruins (ADR §63–§65).</summary>
sealed class NoiseTerrainProvider : ITerrainProvider
{
    private readonly int _seed;

    public NoiseTerrainProvider(int seed) => _seed = seed;

    public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
    {
        var caves = OverworldCaveContext.ForColumn(chunkX, chunkZ, _seed);
        var (subChunkCount, payload) = ChunkPayloads.BuildOverworldColumn(
            chunkX,
            chunkZ,
            (x, y, z) => OverworldTerrainSampler.SampleNoiseBlock(x, y, z, _seed, caves));
        return new TerrainColumn(subChunkCount, payload);
    }

    public int SampleBaseBlock(int x, int y, int z)
        => OverworldTerrainSampler.SampleNoiseBlock(x, y, z, _seed);

    public int SampleSpawnFeetY(int x, int z)
        => OverworldTerrainSampler.SampleSpawnFeetY(x, z, _seed);
}
