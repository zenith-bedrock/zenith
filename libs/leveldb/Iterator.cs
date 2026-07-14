namespace Zenith.LevelDB;

/// <summary>
/// Iterador ordenado sobre um snapshot em memória do dataset (rota B: fonte única = RAM).
/// </summary>
public sealed class Iterator : IDisposable
{
    private readonly SortedDictionary<byte[], byte[]> _data;
    private IEnumerator<KeyValuePair<byte[], byte[]>>? _enum;
    private bool _valid;
    private byte[]? _key;
    private byte[]? _value;
    private bool _disposed;

    internal Iterator(SortedDictionary<byte[], byte[]> dataSnapshot)
    {
        _data = dataSnapshot;
    }

    public bool IsValid() => _valid;

    public byte[]? Key() => _valid ? _key : null;

    public byte[]? Value() => _valid ? _value : null;

    public void Seek(byte[] target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);

        DisposeEnum();
        _valid = false;
        _key = null;
        _value = null;

        _enum = _data.GetEnumerator();
        while (_enum.MoveNext())
        {
            if (ByteComparer.Instance.Compare(_enum.Current.Key, target) >= 0)
            {
                _key = _enum.Current.Key;
                _value = _enum.Current.Value;
                _valid = true;
                return;
            }
        }
    }

    public void SeekToFirst() => Seek([]);

    public void Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_valid || _enum is null) return;

        if (_enum.MoveNext())
        {
            _key = _enum.Current.Key;
            _value = _enum.Current.Value;
            _valid = true;
        }
        else
        {
            _valid = false;
            _key = null;
            _value = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeEnum();
        _valid = false;
    }

    private void DisposeEnum()
    {
        if (_enum is IDisposable d)
            d.Dispose();
        _enum = null;
    }
}
