namespace Zenith.LevelDB;

/// <summary>
/// Key/value managed: dataset completo em RAM + WAL + snapshot único em disco.
/// Formato ZLDB próprio — não compatível com NuGet LevelDB / mundos Mojang.
/// Constraint: o dataset inteiro precisa caber em memória enquanto o DB está aberto.
/// </summary>
public sealed class DB : IDisposable
{
    private readonly string _dir;
    private readonly Options _opts;
    private readonly object _gate = new();
    private readonly MemTable _mem = new();
    private JournalWriter? _journal;
    private string? _tablePath;
    private ulong _nextFileNum = 1;
    private long _dirtyBytes;
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
                {
                    // Stale CURRENT (e.g. leftover NuGet LevelDB MANIFEST) — reset empty.
                    _tablePath = null;
                }
                else if (!TableFile.TryLoadInto(_tablePath, _mem))
                {
                    // Not a Zenith ZLDB snapshot (old native LevelDB residue). Start empty.
                    _tablePath = null;
                }
            }
        }
        else if (!_opts.CreateIfMissing)
        {
            throw new InvalidOperationException($"leveldb: database is missing CURRENT file: {_dir}");
        }

        ReplayJournals();
        _dirtyBytes = 0;

        // Persist non-empty WAL onto snapshot before rotating logs (crash safety).
        if (HasNonEmptyJournalFiles())
            FlushSnapshotUnlocked(sync: true);

        var journalNum = _nextFileNum++;
        _journal = new JournalWriter(Path.Combine(_dir, JournalName(journalNum)), append: false);
        WriteCurrentManifest();
        RemoveOldJournals(keep: journalNum);
        RemoveOrphanTables();
    }

    private bool HasNonEmptyJournalFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_dir, "*.log"))
        {
            try
            {
                if (new FileInfo(path).Length > 0)
                    return true;
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    /// <summary>Enumerates "{ulong number}.{ext}" files in _dir, skipping anything whose stem
    /// isn't a plain number - the one place the {n:D6}.{log,ldb} naming scheme is parsed, used
    /// by every method below that needs to walk numbered journal/table files.</summary>
    private IEnumerable<(ulong Num, string Path)> EnumerateNumberedFiles(string searchPattern)
    {
        foreach (var path in Directory.EnumerateFiles(_dir, searchPattern))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (ulong.TryParse(stem, out var n))
                yield return (n, path);
        }
    }

    private static void TryDeleteBestEffort(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private void RecoverFileNumbers()
    {
        foreach (var (n, _) in EnumerateNumberedFiles("*.ldb").Concat(EnumerateNumberedFiles("*.log")))
        {
            if (n >= _nextFileNum)
                _nextFileNum = n + 1;
        }
    }

    private void ReplayJournals()
    {
        var nums = EnumerateNumberedFiles("*.log").Select(f => f.Num).ToList();
        nums.Sort();
        foreach (var n in nums)
            JournalWriter.Replay(Path.Combine(_dir, JournalName(n)), _mem);
    }

    private void WriteCurrentManifest()
    {
        var tableName = _tablePath is null ? "" : Path.GetFileName(_tablePath);
        var tmp = Path.Combine(_dir, "CURRENT.tmp");
        File.WriteAllText(tmp, tableName + Environment.NewLine);
        var current = Path.Combine(_dir, "CURRENT");
        File.Move(tmp, current, overwrite: true);
    }

    private void RemoveOldJournals(ulong keep)
    {
        foreach (var (n, path) in EnumerateNumberedFiles("*.log"))
        {
            if (n < keep)
                TryDeleteBestEffort(path);
        }
    }

    /// <summary>Best-effort delete of .ldb files not named in CURRENT (mid-flush orphans).</summary>
    private void RemoveOrphanTables()
    {
        var live = _tablePath is null ? null : Path.GetFileName(_tablePath);
        foreach (var path in Directory.EnumerateFiles(_dir, "*.ldb"))
        {
            var name = Path.GetFileName(path);
            if (live is not null && string.Equals(name, live, StringComparison.OrdinalIgnoreCase))
                continue;
            TryDeleteBestEffort(path);
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
            if (!_mem.TryGet(key, out var value, out var deleted) || deleted)
                return null;
            return value is null ? null : (byte[])value.Clone();
        }
    }

    public void Put(byte[] key, byte[] value) => Put(WriteOptions.Default, key, value);

    public void Put(WriteOptions options, byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        var batch = new WriteBatch();
        batch.Put(key, value);
        Write(batch, options);
    }

    public void Delete(byte[] key) => Delete(WriteOptions.Default, key);

    public void Delete(WriteOptions options, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var batch = new WriteBatch();
        batch.Delete(key);
        Write(batch, options);
    }

    public void Write(WriteBatch batch) => Write(batch, WriteOptions.Default);

    public void Write(WriteBatch batch, WriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(options);
        if (batch.Count == 0) return;

        lock (_gate)
        {
            EnsureOpen();
            var encoded = batch.Encode(seq: 0);
            _journal!.AppendBatch(encoded);
            _journal.Flush(options.Sync);
            WriteBatch.ApplyEncoded(encoded, _mem);
            _dirtyBytes += encoded.Length;
            if (_dirtyBytes >= _opts.WriteBufferSize)
                FlushSnapshotUnlocked(sync: true);
        }
    }

    public Iterator CreateIterator() => CreateIterator(ReadOptions.Default);

    public Iterator CreateIterator(ReadOptions options)
    {
        _ = options;
        lock (_gate)
        {
            EnsureOpen();
            var snap = new SortedDictionary<byte[], byte[]>(ByteComparer.Instance);
            foreach (var (k, v) in _mem.LiveEntries())
                snap[(byte[])k.Clone()] = (byte[])v.Clone();
            return new Iterator(snap);
        }
    }

    /// <summary>
    /// Test hook: invoked after the new .ldb is written and fsynced, before CURRENT is published.
    /// Set from leveldb.Tests only (InternalsVisibleTo). Throw to simulate crash mid-flush.
    /// </summary>
    internal static Action? AfterTableWriteBeforeCurrent;

    /// <summary>
    /// Writes live dict → new .ldb (fsync) → publish CURRENT → rotate WAL.
    /// Mem keeps live keys; tombstones dropped. Orphans deleted best-effort.
    /// </summary>
    private void FlushSnapshotUnlocked(bool sync)
    {
        _mem.DropTombstones();
        var live = _mem.LiveEntries();

        var fileNum = _nextFileNum++;
        var newPath = Path.Combine(_dir, TableName(fileNum));
        TableFile.Write(newPath, live, sync);

        AfterTableWriteBeforeCurrent?.Invoke();

        var oldPath = _tablePath;
        _tablePath = newPath;
        WriteCurrentManifest();

        var journalNum = _nextFileNum++;
        _journal?.Dispose();
        _journal = new JournalWriter(Path.Combine(_dir, JournalName(journalNum)), append: false);
        RemoveOldJournals(keep: journalNum);

        if (oldPath is not null && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteBestEffort(oldPath);
        }

        RemoveOrphanTables();
        _dirtyBytes = 0;
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            try
            {
                if (_dirtyBytes > 0)
                    FlushSnapshotUnlocked(sync: true);
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
