namespace Zenith.World;

/// <summary>
/// Mundo: colunas base via <see cref="IChunkStorage"/> + overlay esparso permanente.
/// <see cref="_blockOverrides"/> não tem bound — limitação conhecida nesta escala (sem eviction).
/// Mutação nunca reescreve subchunk; só overlay + UpdateBlock.
/// </summary>
sealed class World
{
    private readonly IChunkStorage _storage;
    private readonly byte[] _flatOverworldPayload;
    private readonly int _flatSubChunkCount;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y, int Z), int> _blockOverrides = new();

    public World(IChunkStorage storage)
    {
        _storage = storage;
        (_flatSubChunkCount, _flatOverworldPayload) = ChunkPayloads.BuildFlatOverworld();
        storage.ForEachOverlayAsync((x, y, z, id) => _blockOverrides[(x, y, z)] = id)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Lê payload base (gera flat on miss / migra empty legado) e aplica overlays da coluna na leitura.
    /// Seguro para chamar da thread de rede.
    /// </summary>
    public async ValueTask<ColumnReadResult> GetOrCreateColumnAsync(int chunkX, int chunkZ, CancellationToken ct = default)
    {
        var coord = new ChunkCoord(chunkX, chunkZ);
        var existing = await _storage.GetAsync(coord, ct).ConfigureAwait(false);

        ChunkColumnData bas;
        if (existing is not null && existing.SubChunkCount > 0)
        {
            bas = existing;
        }
        else
        {
            bas = new ChunkColumnData(coord, dimensionId: 0, _flatSubChunkCount, _flatOverworldPayload);
            await _storage.PutAsync(bas, ct).ConfigureAwait(false);
            bas = (await _storage.GetAsync(coord, ct).ConfigureAwait(false)) ?? bas;
        }

        var overlays = GetOverlaysInColumn(chunkX, chunkZ);
        return new ColumnReadResult(bas, overlays);
    }

    public async ValueTask<IReadOnlyList<ColumnReadResult>> GetRadiusAsync(int centerX, int centerZ, int radius, CancellationToken ct = default)
    {
        var list = new List<ColumnReadResult>((radius * 2 + 1) * (radius * 2 + 1));
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var z = centerZ - radius; z <= centerZ + radius; z++)
                list.Add(await GetOrCreateColumnAsync(x, z, ct).ConfigureAwait(false));
        }

        return list;
    }

    public const int AirRuntimeId = Blocks.Air;

    public void SetBlock(int x, int y, int z, int blockRuntimeId)
    {
        _blockOverrides[(x, y, z)] = blockRuntimeId;
        _storage.PutOverlayAsync(x, y, z, blockRuntimeId).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Overlay se existir; senão amostra do terreno base flat.</summary>
    public int GetBlock(int x, int y, int z)
    {
        if (_blockOverrides.TryGetValue((x, y, z), out var id))
            return id;
        return SampleBaseBlock(x, y, z);
    }

    public IReadOnlyList<BlockOverride> GetOverlaysInColumn(int chunkX, int chunkZ)
    {
        List<BlockOverride>? list = null;
        foreach (var kv in _blockOverrides)
        {
            var (bx, by, bz) = kv.Key;
            if (ToChunk(bx) != chunkX || ToChunk(bz) != chunkZ) continue;
            list ??= new List<BlockOverride>();
            list.Add(new BlockOverride(bx, by, bz, kv.Value));
        }

        return list is null ? Array.Empty<BlockOverride>() : list;
    }

    private static int SampleBaseBlock(int x, int y, int z)
    {
        _ = x;
        _ = z;
        if (y >= Blocks.FlatMinY && y <= Blocks.FlatStoneTopY) return Blocks.Stone;
        if (y == Blocks.FlatGrassY) return Blocks.GrassBlock;
        return Blocks.Air;
    }

    private static int ToChunk(int block) => block >> 4;
}
