namespace Zenith.LevelDB;

/// <summary>
/// A sorted run of entries that can be walked: the memtable copy or one <see cref="Table"/>. A null
/// <see cref="Value"/> is a tombstone. <see cref="MergeIterator"/> combines several of these.
/// </summary>
interface ISortedCursor
{
    bool Valid { get; }
    byte[] Key { get; }
    byte[]? Value { get; }
    void SeekToFirst();

    /// <summary>Positions at the first entry whose key is &gt;= <paramref name="target"/>.</summary>
    void Seek(byte[] target);

    void Next();
}

/// <summary>Cursor over a sorted array (the memtable at the moment a snapshot was taken).</summary>
sealed class ArrayCursor(KeyValuePair<byte[], byte[]?>[] entries) : ISortedCursor
{
    private int _index = -1;

    public bool Valid => _index >= 0 && _index < entries.Length;
    public byte[] Key => entries[_index].Key;
    public byte[]? Value => entries[_index].Value;

    public void SeekToFirst() => _index = 0;

    public void Seek(byte[] target) => _index = LowerBound(entries, target);

    public void Next()
    {
        if (_index < entries.Length) _index++;
    }

    /// <summary>First index whose key is &gt;= <paramref name="target"/>, or the length if there is none.</summary>
    public static int LowerBound(KeyValuePair<byte[], byte[]?>[] entries, ReadOnlySpan<byte> target)
    {
        int lo = 0, hi = entries.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (ByteComparer.Compare(entries[mid].Key, target) < 0) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}

/// <summary>Cursor over one <see cref="Table"/>: reads a block at a time, hands out copies of each entry.</summary>
sealed class TableCursor(Table table) : ISortedCursor
{
    private int _block = -1;
    private byte[] _buffer = [];
    private int _payloadLength;
    private int _position;

    public bool Valid { get; private set; }
    public byte[] Key { get; private set; } = [];
    public byte[]? Value { get; private set; }

    public void SeekToFirst()
    {
        _block = -1;
        Valid = false;
        AdvanceToNextEntry();
    }

    public void Seek(byte[] target)
    {
        Valid = false;
        if (table.BlockCount == 0) return;
        LoadBlock(Math.Max(0, table.FindBlock(target)));
        while (true)
        {
            AdvanceToNextEntry();
            if (!Valid || ByteComparer.Compare(Key, target) >= 0) return;
        }
    }

    public void Next() => AdvanceToNextEntry();

    private void AdvanceToNextEntry()
    {
        if (_block < 0 && !LoadBlock(0)) { Valid = false; return; }
        while (true)
        {
            var payload = _buffer.AsSpan(0, _payloadLength);
            if (Table.TryParseEntry(payload, ref _position, out var key, out var value, out var tombstone))
            {
                Key = key.ToArray();
                Value = tombstone ? null : value.ToArray();
                Valid = true;
                return;
            }

            if (!LoadBlock(_block + 1)) { Valid = false; return; }
        }
    }

    private bool LoadBlock(int index)
    {
        if (index >= table.BlockCount) return false;
        _block = index;
        _payloadLength = table.BlockLength(index);
        if (_buffer.Length < _payloadLength + Table.CrcSize) _buffer = new byte[_payloadLength + Table.CrcSize];
        table.ReadBlock(index, _buffer);
        _position = 0;
        return true;
    }
}

/// <summary>
/// Merges cursors, newest first, into one ordered view: for a key present in several, the newest wins, and a
/// tombstone hides the key from every older one. The shared core of <see cref="Iterator"/> and of compaction.
/// </summary>
sealed class MergeCursor(IReadOnlyList<ISortedCursor> newestFirst, bool skipTombstones) : ISortedCursor
{
    private int _current = -1;

    public bool Valid => _current >= 0;
    public byte[] Key => newestFirst[_current].Key;
    public byte[]? Value => newestFirst[_current].Value;

    public void SeekToFirst()
    {
        foreach (var cursor in newestFirst) cursor.SeekToFirst();
        Settle();
    }

    public void Seek(byte[] target)
    {
        foreach (var cursor in newestFirst) cursor.Seek(target);
        Settle();
    }

    public void Next()
    {
        if (_current < 0) return;
        var key = Key;
        AdvancePast(key);
        Settle();
    }

    private void AdvancePast(byte[] key)
    {
        foreach (var cursor in newestFirst)
            if (cursor.Valid && ByteComparer.Compare(cursor.Key, key) == 0)
                cursor.Next();
    }

    /// <summary>Lands on the smallest key among the cursors, the newest one on a tie.</summary>
    private void Settle()
    {
        while (true)
        {
            var best = -1;
            for (var i = 0; i < newestFirst.Count; i++)
            {
                if (!newestFirst[i].Valid) continue;
                if (best < 0 || ByteComparer.Compare(newestFirst[i].Key, newestFirst[best].Key) < 0)
                    best = i;
            }

            if (best >= 0 && skipTombstones && newestFirst[best].Value is null)
            {
                AdvancePast(newestFirst[best].Key);
                continue;
            }

            _current = best;
            return;
        }
    }
}
