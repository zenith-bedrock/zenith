namespace Zenith.LevelDB;

/// <summary>
/// Ordered cursor over a <see cref="Snapshot"/>: the memtable and every table merged, newest value per key,
/// deleted keys skipped. Each entry is handed out as its own array, valid for as long as the caller keeps it.
/// <para/>
/// Not thread-safe for concurrent calls on the same iterator (ordinary cursor semantics); independent iterators
/// over one snapshot may run on different threads.
/// </summary>
public sealed class Iterator : IDisposable
{
    private readonly MergeCursor _merge;
    private bool _disposed;

    internal Iterator(IReadOnlyList<ISortedCursor> newestFirst) => _merge = new MergeCursor(newestFirst, skipTombstones: true);

    public bool IsValid() => !_disposed && _merge.Valid;

    public ReadOnlyMemory<byte> Key() => IsValid() ? _merge.Key : default;

    public ReadOnlyMemory<byte> Value() => IsValid() ? _merge.Value : default;

    /// <summary>Positions the cursor at the first entry whose key is <c>&gt;= target</c>, or past the end.</summary>
    public void Seek(byte[] target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        _merge.Seek(target);
    }

    public void SeekToFirst()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _merge.SeekToFirst();
    }

    public void Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _merge.Next();
    }

    public void Dispose() => _disposed = true;
}
