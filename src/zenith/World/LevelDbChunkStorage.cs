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
/// <para/>
/// Overlay/chest/inventory/armor/playerdata/world-metadata Puts all enqueue onto one write queue and
/// return without waiting on disk (ADR §41 — GameLoop never waits); a single background worker drains
/// it at a bounded interval (<see cref="WriteWorkerPollMs"/>), not on unbounded thread-pool scheduling
/// (ADR §124 — replaces the earlier per-call <c>Task.Run</c> pattern, which had no bound on how long a
/// queued write could sit unexecuted under thread-pool pressure before a crash).
/// </summary>
sealed class LevelDbChunkStorage : IChunkStorage, IDisposable
{
    /// <summary>Worst-case time a queued write can sit undrained before a crash loses it (ADR §124)
    /// — the worker polls at least this often even with no signal, and every enqueue also signals it
    /// immediately. Matches the interval the earlier overlay-only worker already used.</summary>
    private const int WriteWorkerPollMs = 50;

    private readonly DB _db;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<PendingWrite> _pendingWrites = new();
    private readonly AutoResetEvent _writeSignal = new(false);
    private readonly Thread _writeWorker;
    private volatile bool _stopping;
    private bool _disposed;

    private readonly record struct PendingWrite(byte[] Key, byte[]? Value, bool Delete);

    public LevelDbChunkStorage(string directory)
    {
        Directory.CreateDirectory(directory);
        _db = new DB(new Options { CreateIfMissing = true }, directory);
        _writeWorker = new Thread(WriteWorkerLoop)
        {
            IsBackground = true,
            Name = "LevelDb-Writer"
        };
        _writeWorker.Start();
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
        EnqueueWrite(WorldStorageKeys.Overlay(x, y, z), BitConverter.GetBytes(blockRuntimeId), delete: false);
        return ValueTask.CompletedTask;
    }

    /// <summary>Enfileira Delete de <c>ov:</c> (compactação quando rid == base flat — ADR §36 SoftCap).</summary>
    public ValueTask DeleteOverlayAsync(int x, int y, int z, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnqueueWrite(WorldStorageKeys.Overlay(x, y, z), null, delete: true);
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
        EnqueueWrite(WorldStorageKeys.Chest(x, y, z), blob, delete: false);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteChestAsync(int x, int y, int z, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnqueueWrite(WorldStorageKeys.Chest(x, y, z), null, delete: true);
        return ValueTask.CompletedTask;
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
        EnqueueWrite(WorldStorageKeys.Inventory(uuid), blob, delete: false);
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetInventoryAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]?>(GetKeyBlobAwaitedAsync(WorldStorageKeys.Inventory(uuid), cancellationToken));
    }

    public ValueTask PutArmorAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnqueueWrite(WorldStorageKeys.Armor(uuid), blob, delete: false);
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetArmorAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]?>(GetKeyBlobAwaitedAsync(WorldStorageKeys.Armor(uuid), cancellationToken));
    }

    public ValueTask PutPlayerDataAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnqueueWrite(WorldStorageKeys.PlayerData(uuid), blob, delete: false);
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetPlayerDataAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]?>(GetKeyBlobAwaitedAsync(WorldStorageKeys.PlayerData(uuid), cancellationToken));
    }

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
        var terrainBytes = Encoding.UTF8.GetBytes(metadata.Terrain);
        var blob = new byte[4 + terrainBytes.Length];
        BitConverter.GetBytes(metadata.Seed).CopyTo(blob, 0);
        terrainBytes.CopyTo(blob, 4);
        EnqueueWrite(Encoding.UTF8.GetBytes(WorldStorageKeys.WorldMetadataKey), blob, delete: false);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Force-drains the write queue before reading (S39b / §60 — fast quit→rejoin otherwise races a
    /// still-queued Put and loads a miss). Draining is synchronous and cheap enough to call inline:
    /// it processes whatever's queued right now under <see cref="_gate"/>, the same work the
    /// background worker would do, just not waiting up to <see cref="WriteWorkerPollMs"/> for it.
    /// </summary>
    private Task<byte[]?> GetKeyBlobAwaitedAsync(byte[] key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrainPendingWrites();
            lock (_gate)
            {
                return _db.TryGet(key, out var mem) ? mem.ToArray() : null;
            }
        }, cancellationToken);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _writeSignal.Set();
        // Brief yield so the write worker can drain; then force-drain under gate ourselves too, so
        // FlushAsync's guarantee doesn't depend on the worker thread actually having run by now.
        await Task.Yield();
        DrainPendingWrites();
    }

    private void EnqueueWrite(byte[] key, byte[]? value, bool delete)
    {
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        _pendingWrites.Enqueue(new PendingWrite(key, value, delete));
        _writeSignal.Set();
    }

    private void WriteWorkerLoop()
    {
        while (!_stopping)
        {
            _writeSignal.WaitOne(WriteWorkerPollMs);
            DrainPendingWrites();
        }

        DrainPendingWrites();
    }

    private void DrainPendingWrites()
    {
        while (_pendingWrites.TryDequeue(out var write))
        {
            lock (_gate)
            {
                if (write.Delete)
                    _db.Delete(write.Key);
                else
                    _db.Put(write.Key, write.Value!);
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

    public void Dispose()
    {
        if (_disposed) return;
        _stopping = true;
        _writeSignal.Set();
        _writeWorker.Join(TimeSpan.FromSeconds(5));
        DrainPendingWrites();
        lock (_gate) _db.Close();
        _db.Dispose();
        _writeSignal.Dispose();
        _disposed = true;
    }
}
