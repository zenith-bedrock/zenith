namespace Zenith.World;

/// <summary>
/// Mundo read-only neste marco: gera/cacheia colunas vazias e permite leitura
/// thread-safe fora do tick (PreSpawn). Sem break/place ainda.
/// </summary>
sealed class World
{
    private readonly IChunkStorage _storage;
    private readonly byte[] _emptyOverworldPayload;

    public World(IChunkStorage storage)
    {
        _storage = storage;
        _emptyOverworldPayload = ChunkPayloads.BuildEmptyOverworld();
    }

    /// <summary>
    /// Obtém coluna para (x,z), gerando flat empty on miss e publicando no storage.
    /// Seguro para chamar da thread de rede.
    /// </summary>
    public async ValueTask<ChunkColumnData> GetOrCreateColumnAsync(int chunkX, int chunkZ, CancellationToken ct = default)
    {
        var coord = new ChunkCoord(chunkX, chunkZ);
        var existing = await _storage.GetAsync(coord, ct).ConfigureAwait(false);
        if (existing is not null) return existing;

        var created = new ChunkColumnData(coord, dimensionId: 0, subChunkCount: 0, _emptyOverworldPayload);
        await _storage.PutAsync(created, ct).ConfigureAwait(false);
        return (await _storage.GetAsync(coord, ct).ConfigureAwait(false)) ?? created;
    }

    public async ValueTask<IReadOnlyList<ChunkColumnData>> GetRadiusAsync(int centerX, int centerZ, int radius, CancellationToken ct = default)
    {
        var list = new List<ChunkColumnData>((radius * 2 + 1) * (radius * 2 + 1));
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var z = centerZ - radius; z <= centerZ + radius; z++)
                list.Add(await GetOrCreateColumnAsync(x, z, ct).ConfigureAwait(false));
        }

        return list;
    }

    // --- Mutação fase 3: overlay esparso (estratégia escolhida: NÃO CoW de coluna inteira).
    // Colunas LevelChunk continuam empty; peers veem mudanças via UpdateBlock.

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y, int Z), int> _blockOverrides = new();

    public const int AirRuntimeId = 0;

    public void SetBlock(int x, int y, int z, int blockRuntimeId)
    {
        if (blockRuntimeId == AirRuntimeId)
            _blockOverrides.TryRemove((x, y, z), out _);
        else
            _blockOverrides[(x, y, z)] = blockRuntimeId;
    }

    public int GetBlock(int x, int y, int z) =>
        _blockOverrides.TryGetValue((x, y, z), out var id) ? id : AirRuntimeId;
}
