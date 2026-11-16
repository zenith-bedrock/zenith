namespace Zenith.LevelDB;

/// <summary>The tables of a database, oldest first. Immutable: a change builds a new set and swaps it in.</summary>
sealed class TableSet(Table[] tables)
{
    public static TableSet Empty { get; } = new([]);

    public int Count => tables.Length;
    public Table this[int index] => tables[index];

    public IEnumerable<Table> Oldest => tables;

    public TableSet With(Table added) => new([.. tables, added]);

    /// <summary>This set with <c>count</c> tables from <paramref name="start"/> replaced by <paramref name="merged"/> (null: removed).</summary>
    public TableSet Replace(int start, int count, Table? merged)
    {
        var next = new List<Table>(tables.Length);
        next.AddRange(tables.Take(start));
        if (merged is not null) next.Add(merged);
        next.AddRange(tables.Skip(start + count));
        return new TableSet([.. next]);
    }

    /// <summary>Newest value for <paramref name="key"/> among the tables; <see cref="TableLookup.Deleted"/> stops the search.</summary>
    public TableLookup TryGet(byte[] key, out byte[]? value)
    {
        for (var i = tables.Length - 1; i >= 0; i--)
        {
            var result = tables[i].TryGet(key, out value);
            if (result != TableLookup.NotFound) return result;
        }

        value = null;
        return TableLookup.NotFound;
    }

    public List<ISortedCursor> Cursors(ISortedCursor? first)
    {
        var cursors = new List<ISortedCursor>(tables.Length + 1);
        if (first is not null) cursors.Add(first);
        for (var i = tables.Length - 1; i >= 0; i--) cursors.Add(new TableCursor(tables[i]));
        return cursors;
    }
}

/// <summary>
/// A consistent point-in-time view of the database: the memtable as it was, plus the tables that existed. Both
/// are immutable, so a snapshot costs one array copy of the memtable (bounded by the write buffer), never a copy
/// of the dataset, and later writes cannot reach it.
/// </summary>
public sealed class Snapshot
{
    private readonly KeyValuePair<byte[], byte[]?>[] _memtable;
    private readonly TableSet _tables;

    internal Snapshot(KeyValuePair<byte[], byte[]?>[] memtable, TableSet tables)
    {
        _memtable = memtable;
        _tables = tables;
    }

    /// <summary>Point read against this view; never reflects writes made after the snapshot was taken.</summary>
    public bool TryGet(byte[] key, out ReadOnlyMemory<byte> value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var index = ArrayCursor.LowerBound(_memtable, key);
        if (index < _memtable.Length && ByteComparer.Compare(_memtable[index].Key, key) == 0)
        {
            var stored = _memtable[index].Value;
            value = stored ?? default;
            return stored is not null;
        }

        if (_tables.TryGet(key, out var fromTable) == TableLookup.Found)
        {
            value = fromTable;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>A fresh cursor over this view. Safe to call from any thread.</summary>
    public Iterator CreateIterator() => new(_tables.Cursors(new ArrayCursor(_memtable)));
}
