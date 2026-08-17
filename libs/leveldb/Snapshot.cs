namespace Zenith.LevelDB;

/// <summary>
/// A reusable, consistent point-in-time view of the dataset, materialized once. This is the same
/// copy <see cref="DB.CreateIterator()"/> already builds for isolation from concurrent writes —
/// <see cref="DB.GetSnapshot()"/> just gives that copy a name and lets a caller reuse it for
/// several point reads or iterations without paying the copy cost again per call. Not real MVCC
/// (no sequence numbers, no tombstone filtering) — ZLDB's RAM-resident model makes "clone the live
/// set once" a sufficient, much simpler equivalent for its scope; see <c>README.md</c>'s "why not a
/// full LSM" section for why sequence-numbered snapshots aren't pursued here.
/// <para/>
/// Backed by a sorted array (<see cref="MemTable.LiveEntries"/> already returns entries in
/// <see cref="ByteComparer"/> order, since it iterates the already-sorted
/// <see cref="System.Collections.Immutable.ImmutableSortedDictionary{TKey,TValue}"/>), not a
/// <see cref="SortedDictionary{TKey,TValue}"/> — <see cref="TryGet"/> and
/// <see cref="Iterator.Seek"/> binary-search it directly (O(log n)) instead of relying on a tree
/// lookup / linear scan.
/// </summary>
public sealed class Snapshot
{
    private readonly IReadOnlyList<KeyValuePair<byte[], byte[]>> _entries;

    internal Snapshot(IReadOnlyList<KeyValuePair<byte[], byte[]>> sortedEntries)
    {
        _entries = sortedEntries;
    }

    /// <summary>Point read against this snapshot's view — never reflects writes made after the
    /// snapshot was taken, even if the live <see cref="DB"/> has since changed the key. Zero-alloc
    /// (ADR §118): the array underneath is owned by this snapshot's copy, never cloned per call —
    /// safe under the same copy-on-write discipline <see cref="DB.TryGet"/> relies on. O(log n)
    /// binary search (ADR §120), not a linear scan.</summary>
    public bool TryGet(byte[] key, out ReadOnlyMemory<byte> value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var index = SortedEntrySearch.LowerBound(_entries, key);
        if (index < _entries.Count && ByteComparer.Compare(_entries[index].Key, key) == 0)
        {
            value = _entries[index].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>A fresh <see cref="Iterator"/> over this same materialized view. Cheap relative to
    /// <see cref="DB.CreateIterator()"/> — no new copy, just a new cursor over the existing one.
    /// Safe to call from multiple threads concurrently, and safe to use the resulting iterators
    /// concurrently with each other — this snapshot's entries are never mutated after
    /// construction.</summary>
    public Iterator CreateIterator() => new(_entries);
}

/// <summary>Shared binary-search helper (ADR §120) so <see cref="Snapshot.TryGet"/> and
/// <see cref="Iterator.Seek"/> use one tested implementation instead of two hand-rolled ones.</summary>
internal static class SortedEntrySearch
{
    /// <summary>Index of the first entry whose key is <c>&gt;= target</c>, or
    /// <c>entries.Count</c> if every key sorts before <paramref name="target"/>.</summary>
    public static int LowerBound(IReadOnlyList<KeyValuePair<byte[], byte[]>> entries, ReadOnlySpan<byte> target)
    {
        var lo = 0;
        var hi = entries.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (ByteComparer.Compare(entries[mid].Key, target) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}
