namespace Zenith.World;

readonly record struct ChunkCoord(int X, int Z);

/// <summary>
/// Coluna de chunk pronta pra transmissão (payload de LevelChunk).
/// Imutável após publicação — leituras fora do tick são thread-safe.
/// </summary>
sealed class ChunkColumnData
{
    public ChunkCoord Coord { get; }
    public int DimensionId { get; }
    public int SubChunkCount { get; }
    public byte[] ExtraPayload { get; }

    public ChunkColumnData(ChunkCoord coord, int dimensionId, int subChunkCount, byte[] extraPayload)
    {
        Coord = coord;
        DimensionId = dimensionId;
        SubChunkCount = subChunkCount;
        ExtraPayload = extraPayload;
    }
}

/// <summary>
/// Seam de storage. <see cref="ValueTask"/> desde o dia 1.
/// Overlay esparso (<c>ov:</c>) + chests (<c>ct:</c>) + inventário (<c>inv:</c>) — ADR §39.
/// </summary>
interface IChunkStorage
{
    ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default);
    ValueTask PutAsync(ChunkColumnData column, CancellationToken cancellationToken = default);

    ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default);
    ValueTask ForEachOverlayAsync(Action<int, int, int, int> visitor, CancellationToken cancellationToken = default);

    ValueTask PutChestAsync(int x, int y, int z, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask DeleteChestAsync(int x, int y, int z, CancellationToken cancellationToken = default);
    ValueTask ForEachChestAsync(Action<int, int, int, byte[]> visitor, CancellationToken cancellationToken = default);

    ValueTask PutInventoryAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetInventoryAsync(Guid uuid, CancellationToken cancellationToken = default);

    /// <summary>Await in-flight persistence (chest/inv/overlay). Shutdown only — never GameLoop (ADR §41).</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Cache thread-safe; chunks tratados como imutáveis após Put.
/// Overlay/chest/inv em RAM para testes (ADR §39).
/// </summary>
sealed class InMemoryChunkStorage : IChunkStorage
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ChunkCoord, ChunkColumnData> _chunks = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y, int Z), byte[]> _chests = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _inventories = new();

    /// <summary>Column Put count — tests for ADR §45 sparse flat.</summary>
    public int PutCount { get; private set; }

    public ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(coord, out var column);
        return ValueTask.FromResult<ChunkColumnData?>(column);
    }

    public ValueTask PutAsync(ChunkColumnData column, CancellationToken cancellationToken = default)
    {
        PutCount++;
        _chunks[column.Coord] = column;
        return ValueTask.CompletedTask;
    }

    public ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask ForEachOverlayAsync(Action<int, int, int, int> visitor, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask PutChestAsync(int x, int y, int z, byte[] blob, CancellationToken cancellationToken = default)
    {
        _chests[(x, y, z)] = blob;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteChestAsync(int x, int y, int z, CancellationToken cancellationToken = default)
    {
        _chests.TryRemove((x, y, z), out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask ForEachChestAsync(Action<int, int, int, byte[]> visitor, CancellationToken cancellationToken = default)
    {
        foreach (var (key, blob) in _chests)
            visitor(key.X, key.Y, key.Z, blob);
        return ValueTask.CompletedTask;
    }

    public ValueTask PutInventoryAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        _inventories[uuid] = blob;
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetInventoryAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        _inventories.TryGetValue(uuid, out var blob);
        return ValueTask.FromResult<byte[]?>(blob);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
