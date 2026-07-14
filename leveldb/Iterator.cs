namespace Zenith.LevelDB;

/// <summary>
/// Iterador ordenado. <see cref="Seek"/> / <see cref="Next"/> mesclam memtable (snapshot)
/// com scan streaming da tabela em disco — não materializa o DB inteiro em uma lista.
/// </summary>
public sealed class Iterator : IDisposable
{
    private readonly SortedDictionary<byte[], byte[]?> _mem;
    private readonly string? _tablePath;
    private IEnumerator<(byte[] Key, byte[]? Value)>? _table;
    private IEnumerator<KeyValuePair<byte[], byte[]?>>? _memEnum;
    private bool _tableValid;
    private bool _memValid;
    private byte[]? _key;
    private byte[]? _value;
    private bool _valid;
    private bool _disposed;

    internal Iterator(SortedDictionary<byte[], byte[]?> memSnapshot, string? tablePath)
    {
        _mem = memSnapshot;
        _tablePath = tablePath;
    }

    public bool IsValid() => _valid;

    public byte[]? Key() => _valid ? _key : null;

    public byte[]? Value() => _valid ? _value : null;

    public void Seek(byte[] target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);

        DisposeEnum(_table);
        DisposeEnum(_memEnum);
        _table = null;
        _memEnum = null;
        _tableValid = false;
        _memValid = false;
        _valid = false;
        _key = null;
        _value = null;

        if (_tablePath is not null && File.Exists(_tablePath))
        {
            _table = TableFile.ScanFrom(_tablePath, target).GetEnumerator();
            _tableValid = _table.MoveNext();
        }

        _memEnum = _mem.GetEnumerator();
        // Advance mem to first key >= target
        while (_memEnum.MoveNext())
        {
            if (ByteComparer.Instance.Compare(_memEnum.Current.Key, target) >= 0)
            {
                _memValid = true;
                break;
            }
        }

        AdvanceMerged();
    }

    public void SeekToFirst() => Seek([]);

    public void Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_valid) return;
        AdvanceMerged();
    }

    private void AdvanceMerged()
    {
        while (true)
        {
            byte[]? tableKey = _tableValid ? _table!.Current.Key : null;
            byte[]? memKey = _memValid ? _memEnum!.Current.Key : null;

            if (tableKey is null && memKey is null)
            {
                _valid = false;
                _key = null;
                _value = null;
                return;
            }

            int cmp;
            if (tableKey is null) cmp = 1;
            else if (memKey is null) cmp = -1;
            else cmp = ByteComparer.Instance.Compare(tableKey, memKey);

            if (cmp < 0)
            {
                // table only
                var (k, v) = _table!.Current;
                _tableValid = _table.MoveNext();
                if (v is null) continue; // skip tombstone
                _key = k;
                _value = v;
                _valid = true;
                return;
            }

            if (cmp > 0)
            {
                // mem only
                var kv = _memEnum!.Current;
                _memValid = _memEnum.MoveNext();
                if (kv.Value is null) continue;
                _key = kv.Key;
                _value = kv.Value;
                _valid = true;
                return;
            }

            // same key: mem wins; skip table entry
            var memKv = _memEnum!.Current;
            _memValid = _memEnum.MoveNext();
            _tableValid = _table!.MoveNext();
            if (memKv.Value is null) continue;
            _key = memKv.Key;
            _value = memKv.Value;
            _valid = true;
            return;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeEnum(_table);
        DisposeEnum(_memEnum);
        _valid = false;
    }

    private static void DisposeEnum<T>(IEnumerator<T>? e)
    {
        if (e is IDisposable d)
            d.Dispose();
    }
}
