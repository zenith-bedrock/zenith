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
/// Overlay esparso (<c>ov:</c>) é o formato permanente de edição — coluna = só terreno base.
/// </summary>
interface IChunkStorage
{
    ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default);
    ValueTask PutAsync(ChunkColumnData column, CancellationToken cancellationToken = default);

    ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default);
    ValueTask ForEachOverlayAsync(Action<int, int, int, int> visitor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cache thread-safe; chunks tratados como imutáveis após Put.
/// Overlay em memória só no <see cref="World"/> (InMemory não persiste <c>ov:</c>).
/// </summary>
sealed class InMemoryChunkStorage : IChunkStorage
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ChunkCoord, ChunkColumnData> _chunks = new();

    public ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(coord, out var column);
        return ValueTask.FromResult<ChunkColumnData?>(column);
    }

    public ValueTask PutAsync(ChunkColumnData column, CancellationToken cancellationToken = default)
    {
        _chunks[column.Coord] = column;
        return ValueTask.CompletedTask;
    }

    public ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask ForEachOverlayAsync(Action<int, int, int, int> visitor, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
