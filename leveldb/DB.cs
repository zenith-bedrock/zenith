namespace Zenith.LevelDB;

/// <summary>
/// Banco key/value managed (LSM mínimo: memtable + journal + tabela imutável).
/// Formato on-disk próprio — mundos escritos pelo NuGet LevelDB.Standard precisam ser recriados.
/// </summary>
public sealed class DB : IDisposable
{
    private readonly string _dir;
    private readonly Options _opts;
    private readonly object _gate = new();
    private MemTable _mem = new();
    private JournalWriter? _journal;
    private string? _tablePath;
    private ulong _nextFileNum = 1;
    private bool _closed;

    public DB(Options options, string directory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _opts = options;
        _dir = directory;
        Open();
    }

    public static DB Open(Options options, string directory) => new(options, directory);

    private void Open()
    {
        var existed = Directory.Exists(_dir) && File.Exists(Path.Combine(_dir, "CURRENT"));
        if (existed && _opts.ErrorIfExists)
            throw new InvalidOperationException($"leveldb: database already exists: {_dir}");

        if (!Directory.Exists(_dir))
        {
            if (!_opts.CreateIfMissing)
                throw new InvalidOperationException($"leveldb: database does not exist: {_dir}");
            Directory.CreateDirectory(_dir);
        }

        var lockPath = Path.Combine(_dir, "LOCK");
        if (!File.Exists(lockPath))
            File.WriteAllText(lockPath, "");

        if (!existed && !_opts.CreateIfMissing)
            throw new InvalidOperationException($"leveldb: database is missing CURRENT file: {_dir}");

        RecoverFileNumbers();

        var current = Path.Combine(_dir, "CURRENT");
        if (File.Exists(current))
        {
            var name = File.ReadAllText(current).Trim();
            if (name.Length > 0)
            {
                _tablePath = Path.Combine(_dir, name);
                if (!File.Exists(_tablePath))
                    throw new InvalidDataException($"leveldb: CURRENT points to missing table {_tablePath}");
            }
        }
        else if (!_opts.CreateIfMissing)
        {
            throw new InvalidOperationException($"leveldb: database is missing CURRENT file: {_dir}");
        }

        ReplayJournals();

        // Persist replayed journal entries before rotating logs, so a crash after
        // RemoveOldJournals cannot lose unrecovered writes.
        if (_mem.Count > 0)
            FlushMemtableUnlocked();

        var journalNum = _nextFileNum++;
        _journal = new JournalWriter(Path.Combine(_dir, JournalName(journalNum)), append: false);
        WriteCurrentManifest(journalNum);
        RemoveOldJournals(keep: journalNum);
    }

    private void RecoverFileNumbers()
    {
        foreach (var path in Directory.EnumerateFiles(_dir))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                var stem = Path.GetFileNameWithoutExtension(name);
                if (ulong.TryParse(stem, out var n) && n >= _nextFileNum)
                    _nextFileNum = n + 1;
            }
        }
    }

    private void ReplayJournals()
    {
        var nums = new List<ulong>();
        foreach (var path in Directory.EnumerateFiles(_dir, "*.log"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (ulong.TryParse(stem, out var n))
                nums.Add(n);
        }

        nums.Sort();
        foreach (var n in nums)
            JournalWriter.Replay(Path.Combine(_dir, JournalName(n)), _mem);
    }

    private void WriteCurrentManifest(ulong journalNum)
    {
        // CURRENT = table name (may be empty line if none) — journal tracked only for cleanup.
        var tableName = _tablePath is null ? "" : Path.GetFileName(_tablePath);
        var tmp = Path.Combine(_dir, "CURRENT.tmp");
        File.WriteAllText(tmp, tableName + Environment.NewLine);
        var current = Path.Combine(_dir, "CURRENT");
        File.Move(tmp, current, overwrite: true);
        _ = journalNum;
    }

    private void RemoveOldJournals(ulong keep)
    {
        foreach (var path in Directory.EnumerateFiles(_dir, "*.log"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (ulong.TryParse(stem, out var n) && n < keep)
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
    }

    private static string JournalName(ulong n) => $"{n:D6}.log";
    private static string TableName(ulong n) => $"{n:D6}.ldb";

    public byte[]? Get(byte[] key) => Get(ReadOptions.Default, key);

    public byte[]? Get(ReadOptions options, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = options;
        lock (_gate)
        {
            EnsureOpen();
            if (_mem.TryGet(key, out var memValue, out var deleted))
                return deleted ? null : memValue is null ? null : (byte[])memValue.Clone();

            if (_tablePath is not null && TableFile.TryGet(_tablePath, key, out var tableValue, out var tableDeleted))
                return tableDeleted ? null : tableValue is null ? null : (byte[])tableValue.Clone();

            return null;
        }
    }

    public void Put(byte[] key, byte[] value) => Put(WriteOptions.Default, key, value);

    public void Put(WriteOptions options, byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            EnsureOpen();
            _journal!.AppendPut(key, value);
            _journal.Flush(options.Sync);
            _mem.Put(key, value);
            if (_mem.ApproxSize >= _opts.WriteBufferSize)
                FlushMemtableUnlocked();
        }
    }

    public void Delete(byte[] key) => Delete(WriteOptions.Default, key);

    public void Delete(WriteOptions options, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            EnsureOpen();
            _journal!.AppendDelete(key);
            _journal.Flush(options.Sync);
            _mem.Delete(key);
            if (_mem.ApproxSize >= _opts.WriteBufferSize)
                FlushMemtableUnlocked();
        }
    }

    public Iterator CreateIterator() => CreateIterator(ReadOptions.Default);

    public Iterator CreateIterator(ReadOptions options)
    {
        _ = options;
        lock (_gate)
        {
            EnsureOpen();
            // Snapshot: copy mem entries (values) + table path for streaming Seek.
            var memSnap = new SortedDictionary<byte[], byte[]?>(ByteComparer.Instance);
            foreach (var (k, v) in _mem.Entries)
                memSnap[k] = v is null ? null : (byte[])v.Clone();
            return new Iterator(memSnap, _tablePath);
        }
    }

    private void FlushMemtableUnlocked()
    {
        if (_mem.Count == 0) return;

        // Merge table + mem into one new table (mem wins).
        var merged = new SortedDictionary<byte[], byte[]?>(ByteComparer.Instance);
        if (_tablePath is not null && File.Exists(_tablePath))
        {
            foreach (var kv in TableFile.ReadAll(_tablePath))
                merged[kv.Key] = kv.Value;
        }

        foreach (var (k, v) in _mem.Entries)
            merged[k] = v;

        // Drop tombstones from durable snapshot.
        var live = merged.Where(kv => kv.Value is not null).ToList();

        var fileNum = _nextFileNum++;
        var newPath = Path.Combine(_dir, TableName(fileNum));
        TableFile.Write(newPath, live);

        var oldPath = _tablePath;
        _tablePath = newPath;

        var journalNum = _nextFileNum++;
        _journal?.Dispose();
        _journal = new JournalWriter(Path.Combine(_dir, JournalName(journalNum)), append: false);
        WriteCurrentManifest(journalNum);
        _mem.Clear();

        if (oldPath is not null && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(oldPath); } catch { /* best effort */ }
        }

        RemoveOldJournals(keep: journalNum);
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            try
            {
                if (_mem.Count > 0)
                    FlushMemtableUnlocked();
            }
            finally
            {
                _journal?.Dispose();
                _journal = null;
                _closed = true;
            }
        }
    }

    public void Dispose() => Close();

    private void EnsureOpen()
    {
        if (_closed)
            throw new ObjectDisposedException(nameof(DB));
    }
}
