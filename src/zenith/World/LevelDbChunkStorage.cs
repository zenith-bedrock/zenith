using System.Collections.Concurrent;
using System.Text;
using Zenith.LevelDB;

namespace Zenith.World;

/// <summary>
/// Backend LevelDB — Zenith keys via <see cref="WorldStorageKeys"/> (not Mojang BDS format, though
/// the overlay/chest key layout mirrors BDS's fixed-width chunk-prefix shape — ADR §114).
/// Colunas: <c>c:x:z</c> (unchanged, exact single-key lookup). Overlay/chest keys are now binary,
/// chunk-prefixed (<see cref="WorldStorageKeys.ChunkPrefix"/>), enabling <see cref="LoadOverlaysForChunkAsync"/>
/// / <see cref="LoadChestsForChunkAsync"/> as a Seek + prefix walk instead of a full-table scan.
/// Overlay Puts enfileiram e retornam sem esperar disco (worker único sob <c>_gate</c>).
/// </summary>
sealed class LevelDbChunkStorage : IChunkStorage, IDisposable
{
    private readonly DB _db;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<OverlayWrite> _overlayWrites = new();
    private readonly ConcurrentDictionary<Task, byte> _pendingDiskTasks = new();
    private readonly AutoResetEvent _overlaySignal = new(false);
    private readonly Thread _overlayWorker;
    private volatile bool _stopping;
    private bool _disposed;

    private readonly record struct OverlayWrite(int X, int Y, int Z, int BlockRuntimeId, bool Delete);

    public LevelDbChunkStorage(string directory)
    {
        Directory.CreateDirectory(directory);
        _db = new DB(new Options { CreateIfMissing = true }, directory);
        _overlayWorker = new Thread(OverlayWorkerLoop)
        {
            IsBackground = true,
            Name = "LevelDb-OverlayWriter"
        };
        _overlayWorker.Start();
    }

    public ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ChunkColumnData?>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<byte> mem;
            lock (_gate)
            {
                if (!_db.TryGet(ColumnKey(coord), out mem)) return null;
            }

            var bytes = mem.ToArray();
            if (bytes.Length < 8) return null;
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

    /// <summary>Enfileira o Put; completa quando o item está na fila, não quando o disco terminou.</summary>
    public ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);

        _overlayWrites.Enqueue(new OverlayWrite(x, y, z, blockRuntimeId, Delete: false));
        _overlaySignal.Set();
        return ValueTask.CompletedTask;
    }

    /// <summary>Enfileira Delete de <c>ov:</c> (compactação quando rid == base flat — ADR §36 SoftCap).</summary>
    public ValueTask DeleteOverlayAsync(int x, int y, int z, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);

        _overlayWrites.Enqueue(new OverlayWrite(x, y, z, BlockRuntimeId: 0, Delete: true));
        _overlaySignal.Set();
        return ValueTask.CompletedTask;
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
                it.SeekToFirst();
                while (it.IsValid())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var keyBytes = it.Key().Span;
                    if (WorldStorageKeys.TryParseOverlay(keyBytes, out var x, out var y, out var z))
                    {
                        var value = it.Value().Span;
                        if (value.Length >= 4)
                            visitor(x, y, z, BitConverter.ToInt32(value));
                    }

                    it.Next();
                }
            }
        }, cancellationToken));
    }

    /// <summary>Per-chunk seek: chunkX/chunkZ occupy a fixed 8-byte key prefix (ADR §114), so
    /// finding one chunk's overlays is a Seek + StartsWith walk instead of a full-table scan.</summary>
    public ValueTask<IReadOnlyList<BlockOverride>> LoadOverlaysForChunkAsync(int chunkX, int chunkZ, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<BlockOverride>>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = WorldStorageKeys.ChunkPrefix(chunkX, chunkZ);
            var results = new List<BlockOverride>();
            lock (_gate)
            {
                using var it = _db.CreateIterator();
                it.Seek(prefix);
                while (it.IsValid())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var keyBytes = it.Key().Span;
                    if (!keyBytes.StartsWith(prefix))
                        break;

                    if (WorldStorageKeys.TryParseOverlay(keyBytes, out var x, out var y, out var z))
                    {
                        var value = it.Value().Span;
                        if (value.Length >= 4)
                            results.Add(new BlockOverride(x, y, z, BitConverter.ToInt32(value)));
                    }

                    it.Next();
                }
            }

            return (IReadOnlyList<BlockOverride>)results;
        }, cancellationToken));
    }

    public ValueTask PutChestAsync(int x, int y, int z, byte[] blob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        return Track(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _db.Put(ChestKey(x, y, z), blob);
            }
        }, cancellationToken));
    }

    public ValueTask DeleteChestAsync(int x, int y, int z, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        return Track(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _db.Delete(ChestKey(x, y, z));
            }
        }, cancellationToken));
    }

    public ValueTask ForEachChestAsync(Action<int, int, int, byte[]> visitor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                using var it = _db.CreateIterator();
                it.SeekToFirst();
                while (it.IsValid())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var keyBytes = it.Key().Span;
                    if (WorldStorageKeys.TryParseChest(keyBytes, out var x, out var y, out var z))
                    {
                        var value = it.Value();
                        if (value.Length > 0)
                            visitor(x, y, z, value.ToArray());
                    }

                    it.Next();
                }
            }
        }, cancellationToken));
    }

    /// <summary>Per-chunk seek, mirrors <see cref="LoadOverlaysForChunkAsync"/> (ADR §114).</summary>
    public ValueTask<IReadOnlyList<(int X, int Y, int Z, byte[] Blob)>> LoadChestsForChunkAsync(int chunkX, int chunkZ, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<(int X, int Y, int Z, byte[] Blob)>>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = WorldStorageKeys.ChunkPrefix(chunkX, chunkZ);
            var results = new List<(int, int, int, byte[])>();
            lock (_gate)
            {
                using var it = _db.CreateIterator();
                it.Seek(prefix);
                while (it.IsValid())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var keyBytes = it.Key().Span;
                    if (!keyBytes.StartsWith(prefix))
                        break;

                    if (WorldStorageKeys.TryParseChest(keyBytes, out var x, out var y, out var z))
                    {
                        var value = it.Value();
                        if (value.Length > 0)
                            results.Add((x, y, z, value.ToArray()));
                    }

                    it.Next();
                }
            }

            return (IReadOnlyList<(int X, int Y, int Z, byte[] Blob)>)results;
        }, cancellationToken));
    }

    public ValueTask PutInventoryAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        return Track(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _db.Put(WorldStorageKeys.Inventory(uuid), blob);
            }
        }, cancellationToken));
    }

    public ValueTask<byte[]?> GetInventoryAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]?>(GetUuidBlobAwaitedAsync(WorldStorageKeys.Inventory(uuid), cancellationToken));
    }

    public ValueTask PutArmorAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        return Track(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _db.Put(WorldStorageKeys.Armor(uuid), blob);
            }
        }, cancellationToken));
    }

    public ValueTask<byte[]?> GetArmorAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]?>(GetUuidBlobAwaitedAsync(WorldStorageKeys.Armor(uuid), cancellationToken));
    }

    public ValueTask PutPlayerDataAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        return Track(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _db.Put(WorldStorageKeys.PlayerData(uuid), blob);
            }
        }, cancellationToken));
    }

    public ValueTask<byte[]?> GetPlayerDataAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]?>(GetUuidBlobAwaitedAsync(WorldStorageKeys.PlayerData(uuid), cancellationToken));
    }

    /// <summary>
    /// One fixed key, read/written only at server startup (before any concurrent chunk/overlay
    /// traffic) — no need for the pending-task-await race guard <see cref="GetUuidBlobAwaitedAsync"/>
    /// uses for hot-path player data.
    /// </summary>
    public ValueTask<WorldMetadata?> GetWorldMetadataAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<WorldMetadata?>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<byte> mem;
            lock (_gate)
            {
                if (!_db.TryGet(Encoding.UTF8.GetBytes(WorldStorageKeys.WorldMetadataKey), out mem))
                    return (WorldMetadata?)null;
            }

            var bytes = mem.Span;
            if (bytes.Length < 5) return (WorldMetadata?)null;
            var seed = BitConverter.ToInt32(bytes);
            var terrain = Encoding.UTF8.GetString(bytes.Slice(4));
            return new WorldMetadata(terrain, seed);
        }, cancellationToken));
    }

    public ValueTask PutWorldMetadataAsync(WorldMetadata metadata, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        return Track(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var terrainBytes = Encoding.UTF8.GetBytes(metadata.Terrain);
            var blob = new byte[4 + terrainBytes.Length];
            BitConverter.GetBytes(metadata.Seed).CopyTo(blob, 0);
            terrainBytes.CopyTo(blob, 4);
            lock (_gate)
            {
                _db.Put(Encoding.UTF8.GetBytes(WorldStorageKeys.WorldMetadataKey), blob);
            }
        }, cancellationToken));
    }

    /// <summary>
    /// Await in-flight Puts before read — fast quit→rejoin otherwise races Put and loads miss (S39b / §60).
    /// </summary>
    private async Task<byte[]?> GetUuidBlobAwaitedAsync(byte[] key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = _pendingDiskTasks.Keys.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Still attempt Get — best-effort after failed/canceled Puts.
            }
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return _db.TryGet(key, out var mem) ? mem.ToArray() : null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = _pendingDiskTasks.Keys.ToArray();
        if (pending.Length > 0)
            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);

        _overlaySignal.Set();
        // Brief yield so overlay worker can drain; then force-drain under gate.
        await Task.Yield();
        DrainOverlayWrites();
    }

    private ValueTask Track(Task task)
    {
        _pendingDiskTasks[task] = 0;
        _ = task.ContinueWith(
            t => _pendingDiskTasks.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return new ValueTask(task);
    }

    private void OverlayWorkerLoop()
    {
        while (!_stopping)
        {
            _overlaySignal.WaitOne(50);
            DrainOverlayWrites();
        }

        DrainOverlayWrites();
    }

    private void DrainOverlayWrites()
    {
        while (_overlayWrites.TryDequeue(out var write))
        {
            var key = WorldStorageKeys.Overlay(write.X, write.Y, write.Z);
            lock (_gate)
            {
                if (write.Delete)
                    _db.Delete(key);
                else
                    _db.Put(key, BitConverter.GetBytes(write.BlockRuntimeId));
            }
        }
    }

    public bool TryGetProperty(string property, out string value)
    {
        lock (_gate)
        {
            return _db.GetProperty(property, out value);
        }
    }

    private static byte[] ColumnKey(ChunkCoord coord) => WorldStorageKeys.Column(coord);

    private static byte[] ChestKey(int x, int y, int z) => WorldStorageKeys.Chest(x, y, z);

    public void Dispose()
    {
        if (_disposed) return;
        _stopping = true;
        _overlaySignal.Set();
        _overlayWorker.Join(TimeSpan.FromSeconds(5));
        DrainOverlayWrites();
        lock (_gate) _db.Close();
        _db.Dispose();
        _overlaySignal.Dispose();
        _disposed = true;
    }
}
