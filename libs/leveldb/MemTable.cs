using System.Collections.Immutable;

namespace Zenith.LevelDB;

/// <summary>
/// Dataset completo em RAM. Delete = tombstone até o próximo snapshot.
/// Constrante da rota B: o DB inteiro precisa caber em memória.
/// <para/>
/// Lock-free reads (ADR §119): the live map is an
/// <see cref="ImmutableSortedDictionary{TKey,TValue}"/> behind a single field, published via
/// <see cref="Volatile.Write{T}"/> after each mutation — never mutated in place. A reader does a
/// plain <see cref="Volatile.Read{T}"/> of the current reference and operates on that snapshot;
/// an immutable collection can't be observed half-updated, so no lock is needed on the read side
/// at all. Benchmark evidence (`LevelDbLockPrimitiveBenchmarks`) showed *any* lock around a cheap
/// dictionary lookup — a plain mutex or a <see cref="ReaderWriterLockSlim"/> alike — cost 18-20x
/// throughput at 8 concurrent readers versus no lock; this class removes the read-side lock
/// entirely instead of picking a different lock primitive.
/// <para/>
/// Writes stay serialized by <see cref="DB"/>'s own write lock, which this class does not attempt
/// to route around — that lock exists to keep WAL append and memtable mutation atomic together for
/// durability, not merely to protect this dictionary, so multi-writer lock-freedom here wouldn't
/// remove it. Only one thread ever calls <see cref="Put"/>/<see cref="Delete"/>/
/// <see cref="DropTombstones"/>/<see cref="Clear"/> at a time in practice.
/// </summary>
sealed class MemTable
{
    private ImmutableSortedDictionary<byte[], byte[]?> _map =
        ImmutableSortedDictionary.Create<byte[], byte[]?>(ByteComparer.Instance);

    private long _approxBytes;

    public int Count => Volatile.Read(ref _map).Count;
    public long ApproxSize => Interlocked.Read(ref _approxBytes);

    public void Put(byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        var ownedKey = (byte[])key.Clone();
        var ownedValue = (byte[])value.Clone();

        var current = Volatile.Read(ref _map);
        var delta = KeyValueBytes(ownedKey, ownedValue);
        if (current.TryGetValue(ownedKey, out var prev))
            delta -= KeyValueBytes(ownedKey, prev);

        Volatile.Write(ref _map, current.SetItem(ownedKey, ownedValue));
        Interlocked.Add(ref _approxBytes, delta);
    }

    public void Delete(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var ownedKey = (byte[])key.Clone();

        var current = Volatile.Read(ref _map);
        var delta = KeyValueBytes(ownedKey, null);
        if (current.TryGetValue(ownedKey, out var prev))
            delta -= KeyValueBytes(ownedKey, prev);

        Volatile.Write(ref _map, current.SetItem(ownedKey, null));
        Interlocked.Add(ref _approxBytes, delta);
    }

    public bool TryGet(byte[] key, out byte[]? value, out bool deleted)
    {
        var current = Volatile.Read(ref _map);
        if (current.TryGetValue(key, out var v))
        {
            deleted = v is null;
            value = v;
            return true;
        }

        deleted = false;
        value = null;
        return false;
    }

    public List<KeyValuePair<byte[], byte[]>> LiveEntries()
    {
        var current = Volatile.Read(ref _map);
        var live = new List<KeyValuePair<byte[], byte[]>>(current.Count);
        foreach (var (k, v) in current)
        {
            if (v is not null)
                live.Add(new KeyValuePair<byte[], byte[]>(k, v));
        }

        return live;
    }

    /// <summary>Remove tombstones after a successful snapshot write; keeps live keys in RAM.</summary>
    public void DropTombstones()
    {
        var current = Volatile.Read(ref _map);
        ImmutableSortedDictionary<byte[], byte[]?>.Builder? builder = null;
        foreach (var (k, v) in current)
        {
            if (v is null)
            {
                builder ??= current.ToBuilder();
                builder.Remove(k);
                Interlocked.Add(ref _approxBytes, -KeyValueBytes(k, null));
            }
        }

        if (builder is not null)
            Volatile.Write(ref _map, builder.ToImmutable());
    }

    public void Clear()
    {
        Volatile.Write(ref _map, ImmutableSortedDictionary.Create<byte[], byte[]?>(ByteComparer.Instance));
        Interlocked.Exchange(ref _approxBytes, 0);
    }

    private static long KeyValueBytes(byte[] key, byte[]? value) =>
        key.Length + (value?.Length ?? 0) + 16;
}
