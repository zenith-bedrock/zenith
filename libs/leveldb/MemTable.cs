namespace Zenith.LevelDB;

/// <summary>
/// Dataset completo em RAM (SortedDictionary). Delete = tombstone até o próximo snapshot.
/// Constrante da rota B: o DB inteiro precisa caber em memória.
/// </summary>
sealed class MemTable
{
    private readonly SortedDictionary<byte[], byte[]?> _map = new(ByteComparer.Instance);
    private long _approxBytes;

    public int Count => _map.Count;
    public long ApproxSize => _approxBytes;

    public void Put(byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        var ownedKey = (byte[])key.Clone();
        var ownedValue = (byte[])value.Clone();
        if (_map.TryGetValue(ownedKey, out var prev))
        {
            _approxBytes -= KeyValueBytes(ownedKey, prev);
            _map[ownedKey] = ownedValue;
        }
        else
        {
            _map.Add(ownedKey, ownedValue);
        }

        _approxBytes += KeyValueBytes(ownedKey, ownedValue);
    }

    public void Delete(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var ownedKey = (byte[])key.Clone();
        if (_map.TryGetValue(ownedKey, out var prev))
        {
            _approxBytes -= KeyValueBytes(ownedKey, prev);
            _map[ownedKey] = null;
        }
        else
        {
            _map.Add(ownedKey, null);
        }

        _approxBytes += KeyValueBytes(ownedKey, null);
    }

    public bool TryGet(byte[] key, out byte[]? value, out bool deleted)
    {
        if (_map.TryGetValue(key, out var v))
        {
            deleted = v is null;
            value = v;
            return true;
        }

        deleted = false;
        value = null;
        return false;
    }

    public IEnumerable<KeyValuePair<byte[], byte[]?>> Entries => _map;

    public List<KeyValuePair<byte[], byte[]>> LiveEntries()
    {
        var live = new List<KeyValuePair<byte[], byte[]>>();
        foreach (var (k, v) in _map)
        {
            if (v is not null)
                live.Add(new KeyValuePair<byte[], byte[]>(k, v));
        }

        return live;
    }

    /// <summary>Remove tombstones after a successful snapshot write; keeps live keys in RAM.</summary>
    public void DropTombstones()
    {
        List<byte[]>? dead = null;
        foreach (var (k, v) in _map)
        {
            if (v is null)
            {
                dead ??= new List<byte[]>();
                dead.Add(k);
            }
        }

        if (dead is null) return;
        foreach (var k in dead)
        {
            _approxBytes -= KeyValueBytes(k, null);
            _map.Remove(k);
        }
    }

    public void Clear()
    {
        _map.Clear();
        _approxBytes = 0;
    }

    private static long KeyValueBytes(byte[] key, byte[]? value) =>
        key.Length + (value?.Length ?? 0) + 16;
}
