using System.Collections.Concurrent;

namespace Zenith.World;

/// <summary>
/// <see cref="IChunkStorage"/> that never makes the caller wait on the disk (ADR §41, §168). A put lands in a
/// concurrent map and returns; one worker thread writes what has accumulated as a single atomic batch, at least
/// every <see cref="DrainIntervalMs"/>. Several puts of one key between drains cost one write (the newest wins).
/// <para/>
/// Reads see their own writes: a pending record is returned from memory, anything else comes from the store on a
/// pool thread. That is why nothing needs to force a drain before a read.
/// </summary>
class WriteBehindStorage : IChunkStorage, IDisposable
{
    /// <summary>Worst-case time a put can sit unwritten before a crash loses it (ADR §124).</summary>
    public const int DrainIntervalMs = 50;

    private readonly IRecordStore _store;
    private readonly ConcurrentDictionary<RecordKey, byte[]> _pending = new();
    private readonly Lock _drainGate = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private volatile bool _stopping;
    private bool _disposed;

    public WriteBehindStorage(IRecordStore store)
    {
        _store = store;
        _worker = new Thread(Run) { IsBackground = true, Name = "WriteBehind" };
        _worker.Start();
    }

    public ValueTask<byte[]?> GetAsync(RecordKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pending.TryGetValue(key, out var pending))
            return ValueTask.FromResult<byte[]?>(pending);

        return new ValueTask<byte[]?>(
            Task.Run(() => _pending.TryGetValue(key, out var raced) ? raced : _store.Get(key), cancellationToken));
    }

    public ValueTask PutAsync(RecordKey key, byte[] blob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
        _pending[key] = blob;
        _signal.Set();
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Drain(durable: true);
        return ValueTask.CompletedTask;
    }

    public bool TryGetProperty(string property, out string value) => _store.TryGetProperty(property, out value);

    private void Run()
    {
        while (!_stopping)
        {
            _signal.WaitOne(DrainIntervalMs);
            Drain(durable: false);
        }
    }

    private void Drain(bool durable)
    {
        lock (_drainGate)
        {
            if (_pending.IsEmpty && !durable) return;
            var batch = _pending.ToArray();
            _store.Write(batch, durable);

            // Remove only what was written: a newer value put meanwhile stays pending for the next drain.
            foreach (var entry in batch)
                ((ICollection<KeyValuePair<RecordKey, byte[]>>)_pending).Remove(entry);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _stopping = true;
        _signal.Set();
        _worker.Join(TimeSpan.FromSeconds(5));
        Drain(durable: true);
        _store.Dispose();
        _signal.Dispose();
        _disposed = true;
    }
}
