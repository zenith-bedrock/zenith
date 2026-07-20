namespace Zenith.World;

/// <summary>Base column from terrain (ADR §62) — not overlays, not storage.</summary>
readonly record struct TerrainColumn(int SubChunkCount, byte[] Payload);

/// <summary>
/// Supplies base terrain for column miss / heal and SoftCap base-rid sampling.
/// Default: <see cref="FlatTerrainProvider"/>. Future gen plugs here; BDS stays on <see cref="IChunkStorage"/> (§61).
/// </summary>
interface ITerrainProvider
{
    TerrainColumn GetBaseColumn(int chunkX, int chunkZ);

    /// <summary>Block at cell if no overlay — must match <see cref="GetBaseColumn"/> semantics.</summary>
    int SampleBaseBlock(int x, int y, int z);
}

/// <summary>Classic Zenith flat overworld (stone / grass / air).</summary>
sealed class FlatTerrainProvider : ITerrainProvider
{
    public static FlatTerrainProvider Instance { get; } = new();

    private readonly int _subChunkCount;
    private readonly byte[] _payload;

    private FlatTerrainProvider()
    {
        (_subChunkCount, _payload) = ChunkPayloads.BuildFlatOverworld();
    }

    public TerrainColumn GetBaseColumn(int chunkX, int chunkZ)
    {
        _ = chunkX;
        _ = chunkZ;
        return new TerrainColumn(_subChunkCount, _payload);
    }

    public int SampleBaseBlock(int x, int y, int z)
    {
        _ = x;
        _ = z;
        if (y >= Blocks.FlatMinY && y <= Blocks.FlatStoneTopY) return Blocks.Stone;
        if (y == Blocks.FlatGrassY) return Blocks.GrassBlock;
        return Blocks.Air;
    }
}
