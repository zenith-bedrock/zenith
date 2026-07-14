using System.Text;
using LevelDB;

namespace Zenith.World;

/// <summary>
/// Backend LevelDB (chaves Zenith, não formato vanilla Mojang).
/// I/O em ThreadPool via <see cref="Task.Run"/> para não bloquear a thread de rede.
/// </summary>
sealed class LevelDbChunkStorage : IChunkStorage, IDisposable
{
    private readonly DB _db;
    private readonly object _gate = new();
    private bool _disposed;

    public LevelDbChunkStorage(string directory)
    {
        Directory.CreateDirectory(directory);
        _db = new DB(new Options { CreateIfMissing = true }, directory);
    }

    public ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ChunkColumnData?>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? bytes;
            lock (_gate)
            {
                bytes = _db.Get(Key(coord));
            }

            if (bytes is null || bytes.Length < 8) return null;
            var subChunkCount = BitConverter.ToInt32(bytes, 0);
            var dimensionId = BitConverter.ToInt32(bytes, 4);
            var payload = bytes.AsSpan(8).ToArray();
            return new ChunkColumnData(coord, dimensionId, subChunkCount, payload);
        }, cancellationToken));
    }

    public ValueTask PutAsync(ChunkColumnData column, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blob = new byte[8 + column.ExtraPayload.Length];
            BitConverter.TryWriteBytes(blob.AsSpan(0, 4), column.SubChunkCount);
            BitConverter.TryWriteBytes(blob.AsSpan(4, 4), column.DimensionId);
            column.ExtraPayload.CopyTo(blob.AsSpan(8));
            lock (_gate)
            {
                _db.Put(Key(column.Coord), blob);
            }
        }, cancellationToken));
    }

    private static byte[] Key(ChunkCoord coord) =>
        Encoding.UTF8.GetBytes($"c:{coord.X}:{coord.Z}");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate) _db.Close();
        _db.Dispose();
    }
}
