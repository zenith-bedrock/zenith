namespace Zenith.World;

/// <summary>
/// A freshly generated column's sections (<see cref="WorldColumn.SectionCount"/> slots, null above the last generated one)
/// and biome. The caller owns the sections: they are mutable, so a provider must hand out new instances every time.
/// </summary>
readonly record struct TerrainColumn(Section?[] Sections, int BiomeNetworkId);

/// <summary>
/// Generates the columns the world has no stored copy of. Default: <see cref="FlatTerrainProvider"/>.
/// </summary>
interface ITerrainProvider
{
    TerrainColumn GetBaseColumn(int chunkX, int chunkZ);

    /// <summary>The generated block at a cell — must match <see cref="GetBaseColumn"/>; tests and tools only, the world reads its columns.</summary>
    int SampleBaseBlock(int x, int y, int z);

    /// <summary>Domain feet Y for first join / respawn above base surface at (x,z).</summary>
    int SampleSpawnFeetY(int x, int z);

    /// <summary>Biome at a column. Flat terrain is all plains; noise terrain reads its climate.</summary>
    OverworldBiomeKind SampleBiome(int x, int z) => OverworldBiomeKind.Plains;
}

/// <summary>Classic Zenith flat overworld (stone / grass / air).</summary>
sealed class FlatTerrainProvider : ITerrainProvider
{
    public static FlatTerrainProvider Instance { get; } = new();

    private FlatTerrainProvider()
    {
    }

    public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
    {
        _ = chunkX;
        _ = chunkZ;
        return ChunkPayloads.BuildFlatOverworld();
    }

    public int SampleBaseBlock(int x, int y, int z)
        => OverworldTerrainSampler.SampleBlock(x, y, z, Blocks.FlatGrassY);

    public int SampleSpawnFeetY(int x, int z)
    {
        _ = x;
        _ = z;
        return Blocks.FlatSpawnY;
    }
}
