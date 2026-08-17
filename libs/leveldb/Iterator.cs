namespace Zenith.LevelDB;

/// <summary>
/// Ordered cursor over an in-memory <see cref="Snapshot"/> of the dataset (rota B: fonte única =
/// RAM). Backed by a sorted array/list (ADR §120), not a <see cref="SortedDictionary{TKey,TValue}"/>
/// — <see cref="Seek"/> binary-searches it directly (O(log n)); the previous
/// <see cref="SortedDictionary{TKey,TValue}"/>-backed design had no way to seek without a full
/// O(n) linear scan from the start, since that type exposes no binary-searchable view.
/// <para/>
/// Not thread-safe for concurrent calls on the *same* iterator instance (ordinary cursor
/// semantics), but multiple independent iterators over the same <see cref="Snapshot"/> — even used
/// concurrently from different threads — are safe: the underlying entries are never mutated once
/// the snapshot is built.
/// </summary>
public sealed class Iterator : IDisposable
{
    private readonly IReadOnlyList<KeyValuePair<byte[], byte[]>> _entries;
    private int _index;
    private bool _disposed;

    internal Iterator(IReadOnlyList<KeyValuePair<byte[], byte[]>> sortedEntries)
    {
        _entries = sortedEntries;
        _index = -1;
    }

    public bool IsValid() => !_disposed && _index >= 0 && _index < _entries.Count;

    /// <summary>Zero-alloc (ADR §118): the array underneath is owned by the snapshot this iterator
    /// was built from, never cloned per call.</summary>
    public ReadOnlyMemory<byte> Key() => IsValid() ? _entries[_index].Key : default;

    /// <summary>See <see cref="Key"/>.</summary>
    public ReadOnlyMemory<byte> Value() => IsValid() ? _entries[_index].Value : default;

    /// <summary>Positions the cursor at the first entry whose key is <c>&gt;= target</c>, or past
    /// the end (<see cref="IsValid"/> false) if none. O(log n) binary search (ADR §120).</summary>
    public void Seek(byte[] target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        _index = SortedEntrySearch.LowerBound(_entries, target);
    }

    public void SeekToFirst()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _index = 0;
    }

    public void Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_index < _entries.Count)
            _index++;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
