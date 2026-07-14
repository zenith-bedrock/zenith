using System.Text;
using Zenith.LevelDB;

namespace Zenith.World;

/// <summary>
/// Backend LevelDB (chaves Zenith, não formato vanilla Mojang).
/// Colunas: <c>c:x:z</c>. Overlay permanente: <c>ov:x:y:z</c> → int32 LE runtime id.
/// I/O em ThreadPool via <see cref="Task.Run"/> para não bloquear a thread de rede.
/// </summary>
sealed class LevelDbChunkStorage : IChunkStorage, IDisposable
{
    private static readonly byte[] OverlayPrefix = Encoding.UTF8.GetBytes("ov:");

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
                bytes = _db.Get(ColumnKey(coord));
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
                _db.Put(ColumnKey(column.Coord), blob);
            }
        }, cancellationToken));
    }

    public ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = OverlayKey(x, y, z);
            var value = BitConverter.GetBytes(blockRuntimeId);
            lock (_gate)
            {
                _db.Put(key, value);
            }
        }, cancellationToken));
    }

    public ValueTask ForEachOverlayAsync(Action<int, int, int, int> visitor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                using var it = _db.CreateIterator();
                it.Seek(OverlayPrefix);
                while (it.IsValid())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var keyBytes = it.Key();
                    if (keyBytes is null || keyBytes.Length < OverlayPrefix.Length)
                        break;
                    if (!StartsWith(keyBytes, OverlayPrefix))
                        break;

                    var key = Encoding.UTF8.GetString(keyBytes);
                    if (!TryParseOverlayKey(key, out var x, out var y, out var z))
                    {
                        it.Next();
                        continue;
                    }

                    var value = it.Value();
                    if (value is not null && value.Length >= 4)
                        visitor(x, y, z, BitConverter.ToInt32(value, 0));

                    it.Next();
                }
            }
        }, cancellationToken));
    }

    private static byte[] ColumnKey(ChunkCoord coord) =>
        Encoding.UTF8.GetBytes($"c:{coord.X}:{coord.Z}");

    private static byte[] OverlayKey(int x, int y, int z) =>
        Encoding.UTF8.GetBytes($"ov:{x}:{y}:{z}");

    private static bool TryParseOverlayKey(string key, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (!key.StartsWith("ov:", StringComparison.Ordinal)) return false;
        var parts = key.AsSpan(3).ToString().Split(':');
        if (parts.Length != 3) return false;
        return int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && int.TryParse(parts[2], out z);
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i]) return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate) _db.Close();
        _db.Dispose();
    }
}
