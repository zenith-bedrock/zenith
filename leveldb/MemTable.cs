namespace Zenith.LevelDB;

/// <summary>Memtable ordenado em RAM. Delete é tombstone (value null).</summary>
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

    public void Clear()
    {
        _map.Clear();
        _approxBytes = 0;
    }

    private static long KeyValueBytes(byte[] key, byte[]? value) =>
        key.Length + (value?.Length ?? 0) + 16;
}
