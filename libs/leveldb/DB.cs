namespace Zenith.LevelDB;

/// <summary>
/// ZLDB (ADR §11, §168): an ordered key-value store in the LevelDB shape. Writes go to a write-ahead log and an
/// in-memory memtable; when the memtable reaches <see cref="Options.WriteBufferSize"/> it is written as one new
/// immutable <see cref="Table"/>, and background size-tiered compaction merges neighbouring tables. Only the memtable
/// and a small index per table live in RAM, so the database can be much larger than memory, and a flush costs the
/// size of the buffer, never of the database. Not file-compatible with Mojang or Google LevelDB.
/// </summary>
public sealed class DB : IDisposable
{
    private readonly string _dir;
    private readonly Options _opts;

    /// <summary>
    /// Serializes writers: WAL append and memtable update stay atomic together, and the table set changes only
    /// under it. Readers take no lock (ADR §119): the memtable and the table set are immutable values behind one
    /// reference each, and tables are read-only files. A reader reads the memtable first and the table set second;
    /// a flush publishes the new table set before it empties the memtable, so a reader never misses a key.
    /// </summary>
    private readonly Lock _writeGate = new();

    private readonly MemTable _mem = new();
    private readonly CancellationTokenSource _stopCompaction = new();
    private JournalWriter? _journal;
    private FileStream? _lockHandle;
    private ulong _nextFileNum = 1;
    private bool _compactionRunning;
    private Task? _compactionTask;
    private volatile TableSet _tables = TableSet.Empty;

    /// <summary>Volatile: read by <see cref="EnsureOpen"/> from lock-free readers, written by <see cref="Close"/> under the gate.</summary>
    private volatile bool _closed;

    public DB(Options options, string directory) : this(options, directory, tolerateCorruptSnapshot: false)
    {
    }

    private DB(Options options, string directory, bool tolerateCorruptSnapshot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _opts = options;
        _dir = directory;
        try
        {
            Open(tolerateCorruptSnapshot);
        }
        catch
        {
            // Open may throw after taking the OS lock (e.g. a corrupted table): release it so a failed open does
            // not lock the directory out of a later attempt, including Repair.
            _journal?.Dispose();
            foreach (var table in _tables.Oldest) table.Dispose();
            _lockHandle?.Dispose();
            throw;
        }
    }

    public static DB Open(Options options, string directory) => new(options, directory);

    /// <summary>
    /// Deletes exactly the files ZLDB itself owns in <paramref name="directory"/> (<c>LOCK</c>,
    /// <c>CURRENT</c>/<c>CURRENT.tmp</c>, numbered <c>*.log</c>/<c>*.ldb</c>) rather than the
    /// directory wholesale, so a caller doesn't need to trust that <paramref name="directory"/>
    /// contains nothing else worth keeping. Removes the directory itself only if it ends up empty.
    /// No-op if the directory doesn't exist. Mirrors real LevelDB's <c>DestroyDB</c>.
    /// <para/>
    /// Refuses (throws) rather than proceeding if another <see cref="DB"/> instance/process
    /// currently has <paramref name="directory"/> open (ADR §120) — probed the same way
    /// <see cref="Open()"/> acquires its exclusive lock. Without this check, the per-file
    /// best-effort deletes below would silently swallow the resulting sharing-violation
    /// exceptions, so a caller could believe <c>Destroy</c> succeeded while it actually left the
    /// other instance's directory partially deleted out from under it while that instance keeps
    /// running.
    /// </summary>
    public static void Destroy(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) return;

        var lockPath = Path.Combine(directory, "LOCK");
        if (File.Exists(lockPath))
        {
            try
            {
                using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"leveldb: cannot destroy {directory} — locked by another DB instance/process", ex);
            }
        }

        TryDeleteBestEffort(lockPath);
        TryDeleteBestEffort(Path.Combine(directory, "CURRENT"));
        TryDeleteBestEffort(Path.Combine(directory, "CURRENT.tmp"));
        foreach (var path in Directory.EnumerateFiles(directory, "*.log"))
            TryDeleteBestEffort(path);
        foreach (var path in Directory.EnumerateFiles(directory, "*.ldb"))
            TryDeleteBestEffort(path);
        foreach (var path in Directory.EnumerateFiles(directory, "*.tmp"))
            TryDeleteBestEffort(path);

        try
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch
        {
            // Best-effort: leave the directory if non-ZLDB files remain or deletion fails.
        }
    }

    /// <summary>
    /// Attempts to bring an unopenable ZLDB directory back to an openable state, salvaging what it
    /// can. Never invoked automatically by <see cref="Open()"/> or the public constructor — a
    /// corrupted database always fails loudly by default (see <see cref="Options.OnCorruption"/>
    /// and <see cref="TableFile.TryLoadInto"/>'s thrown <see cref="InvalidDataException"/>); repair is
    /// an explicit, separate action an operator opts into, not a silent fallback that could mask
    /// real corruption as normal operation.
    /// <para/>
    /// Scope, honestly: repair works per table, not per block. Every table is read in full; one that fails a
    /// checksum is dropped whole (its keys are lost, the other tables are kept), and the log is replayed on top
    /// (everything before a corrupt log record is kept, as in a normal open). It rewrites a valid
    /// <c>CURRENT</c> and log as a side effect, leaving the directory openable.
    /// </summary>
    public static void Repair(string directory, Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) return; // nothing to repair

        var repairOptions = new Options
        {
            CreateIfMissing = true,
            ErrorIfExists = false,
            WriteBufferSize = options.WriteBufferSize,
        };

        using var db = new DB(repairOptions, directory, tolerateCorruptSnapshot: true);
    }

    private void Open(bool tolerateCorruptSnapshot)
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

        // A real OS-level exclusive lock for the DB's whole lifetime, not a marker file: two instances on one
        // directory, in one process or two, would each keep their own memtable and journal and clobber each other.
        var lockPath = Path.Combine(_dir, "LOCK");
        try
        {
            _lockHandle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"leveldb: database is locked by another DB instance/process: {_dir}", ex);
        }

        if (!existed && !_opts.CreateIfMissing)
            throw new InvalidOperationException($"leveldb: database is missing CURRENT file: {_dir}");

        RecoverFileNumbers();
        LoadTables(tolerateCorruptSnapshot);
        ReplayJournals();

        lock (_writeGate)
        {
            // What the log still held becomes a table now, so the log can start empty.
            if (_mem.Count > 0)
                FlushMemTableUnlocked(sync: true);
            else
                RotateJournal();
            RemoveOrphanFiles();
            ScheduleCompaction();
        }
    }

    /// <summary>
    /// Opens the tables <c>CURRENT</c> lists (oldest first, one name per line). A name that looks like one of
    /// ours but is missing, or a table failing a checksum, is data loss and fails loudly; only <see cref="Repair"/>
    /// tolerates it. A listed file that is not a ZLDB table at all is foreign residue and is skipped.
    /// </summary>
    private void LoadTables(bool tolerateCorruption)
    {
        var current = Path.Combine(_dir, "CURRENT");
        if (!File.Exists(current))
        {
            if (!_opts.CreateIfMissing)
                throw new InvalidOperationException($"leveldb: database is missing CURRENT file: {_dir}");
            return;
        }

        var tables = new List<Table>();
        foreach (var name in File.ReadAllLines(current).Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            var path = Path.Combine(_dir, name);
            if (!File.Exists(path))
            {
                if (LooksLikeZldbTableName(name) && !tolerateCorruption)
                    throw new InvalidDataException(
                        $"leveldb: CURRENT references {name}, which looks like a ZLDB table " +
                        $"but is missing from disk — refusing to silently start empty: {_dir}");
                continue;
            }

            try
            {
                if (Table.TryOpen(path, out var table))
                {
                    // Repair reads every block, so a flipped byte inside one is found now, not by a later read.
                    if (tolerateCorruption)
                    {
                        try { table!.Verify(); }
                        catch { table!.Dispose(); throw; }
                    }

                    tables.Add(table!);
                }
            }
            catch (InvalidDataException) when (tolerateCorruption)
            {
                // Repair: a damaged table is dropped, the rest and the log are kept.
            }
        }

        _tables = new TableSet([.. tables]);
    }

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
            JournalWriter.Replay(Path.Combine(_dir, JournalName(n)), _mem, _opts.OnCorruption);
    }

    /// <summary>Publishes the current table list. Written to a temporary name and renamed, so it is never half there.</summary>
    private void WriteCurrentManifest()
    {
        var tmp = Path.Combine(_dir, "CURRENT.tmp");
        File.WriteAllLines(tmp, _tables.Oldest.Select(t => t.Name));
        File.Move(tmp, Path.Combine(_dir, "CURRENT"), overwrite: true);
    }

    /// <summary>Starts a new, empty log, publishes <c>CURRENT</c> and removes the logs the tables now cover.</summary>
    private void RotateJournal()
    {
        var journalNum = _nextFileNum++;
        _journal?.Dispose();
        _journal = new JournalWriter(Path.Combine(_dir, JournalName(journalNum)), append: false);
        try
        {
            WriteCurrentManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LevelDbIOException($"leveldb: CURRENT publish failed for {_dir}", ex);
        }

        foreach (var (n, path) in EnumerateNumberedFiles("*.log"))
        {
            if (n < journalNum)
                TryDeleteBestEffort(path);
        }
    }

    /// <summary>Best-effort delete of tables not listed in <c>CURRENT</c> (a crash between writing and publishing) and of leftovers of unfinished writes.</summary>
    private void RemoveOrphanFiles()
    {
        var live = _tables.Oldest.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_dir, "*.ldb"))
        {
            if (!live.Contains(Path.GetFileName(path)))
                TryDeleteBestEffort(path);
        }

        foreach (var path in Directory.EnumerateFiles(_dir, "*.tmp"))
        {
            if (!string.Equals(Path.GetFileName(path), "CURRENT.tmp", StringComparison.OrdinalIgnoreCase))
                TryDeleteBestEffort(path);
        }
    }

    private static string JournalName(ulong n) => $"{n:D6}.log";
    private static string TableName(ulong n) => $"{n:D6}.ldb";

    /// <summary>True if <paramref name="name"/> has the shape ZLDB gives its own tables (numeric stem, <c>.ldb</c>):
    /// a missing one is data loss (§121); a missing foreign name (e.g. a leftover <c>MANIFEST-…</c>) is harmless.</summary>
    private static bool LooksLikeZldbTableName(string name) =>
        string.Equals(Path.GetExtension(name), ".ldb", StringComparison.OrdinalIgnoreCase) &&
        ulong.TryParse(Path.GetFileNameWithoutExtension(name), out _);

    public bool TryGet(byte[] key, out ReadOnlyMemory<byte> value) => TryGet(ReadOptions.Default, key, out value);

    /// <summary>
    /// Lock-free point read: the memtable first, then the tables newest to oldest, a tombstone ending the search.
    /// A value from the memtable is the array the memtable owns (never cloned, never mutated in place), so callers
    /// must not change it. A value from a table is a fresh array. The key stays <c>byte[]</c> because the memtable's
    /// sorted map cannot look up by span without allocating.
    /// </summary>
    public bool TryGet(ReadOptions options, byte[] key, out ReadOnlyMemory<byte> value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = options;
        EnsureOpen();
        if (_mem.TryGet(key, out var stored, out var deleted))
        {
            value = stored ?? default;
            return !deleted && stored is not null;
        }

        if (_tables.TryGet(key, out var fromTable) == TableLookup.Found)
        {
            value = fromTable;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Named introspection values for diagnostics, never gameplay. False for an unknown name.
    /// <c>zldb.num-entries</c>: entries in the memtable plus every table, tombstones and overwritten versions
    /// included until compaction folds them, so an upper bound on the live keys.
    /// <c>zldb.num-tables</c>: tables on disk. <c>zldb.approximate-memory-usage</c>: bytes held in RAM by the memtable
    /// and the tables' indexes and bloom filters.
    /// </summary>
    public bool GetProperty(string property, out string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureOpen();
        var tables = _tables;
        switch (property)
        {
            case "zldb.num-entries":
                value = (_mem.Count + tables.Oldest.Sum(t => t.EntryCount)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case "zldb.num-tables":
                value = tables.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case "zldb.approximate-memory-usage":
                value = (_mem.ApproxSize + tables.Oldest.Sum(t => t.ResidentBytes)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            default:
                value = "";
                return false;
        }
    }

    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => Put(WriteOptions.Default, key, value);

    public void Put(WriteOptions options, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var batch = new WriteBatch();
        batch.Put(key, value);
        Write(batch, options);
    }

    public void Delete(ReadOnlySpan<byte> key) => Delete(WriteOptions.Default, key);

    public void Delete(WriteOptions options, ReadOnlySpan<byte> key)
    {
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

        lock (_writeGate)
        {
            EnsureOpen();
            var encoded = batch.Encode(seq: 0);
            try
            {
                _journal!.AppendBatch(encoded);
                _journal.Flush(options.Sync);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new LevelDbIOException($"leveldb: WAL append/flush failed for {_dir}", ex);
            }

            WriteBatch.ApplyEncoded(encoded, _mem);
            if (_mem.ApproxSize >= _opts.WriteBufferSize)
                FlushMemTableUnlocked(sync: true);
        }
    }

    public Iterator CreateIterator() => CreateIterator(ReadOptions.Default);

    public Iterator CreateIterator(ReadOptions options) => GetSnapshot(options).CreateIterator();

    public Snapshot GetSnapshot() => GetSnapshot(ReadOptions.Default);

    /// <summary>A point-in-time view: the memtable as it is now (one array copy, bounded by the write buffer) and the current tables.</summary>
    public Snapshot GetSnapshot(ReadOptions options)
    {
        _ = options;
        EnsureOpen();
        var memtable = _mem.Entries(); // memtable before tables: see the note on _writeGate
        return new Snapshot(memtable, _tables);
    }

    /// <summary>
    /// Test hook: invoked after the new table is written and fsynced, before it is published. Set from
    /// leveldb.Tests only (InternalsVisibleTo). Throw to simulate a crash mid-flush.
    /// </summary>
    internal static Action? AfterTableWriteBeforeCurrent;

    /// <summary>
    /// Writes the memtable as a new table, publishes it, then empties the memtable and starts a new log. Only the
    /// memtable is written, so the cost is the buffer's size. The caller holds <see cref="_writeGate"/>.
    /// </summary>
    private void FlushMemTableUnlocked(bool sync)
    {
        var entries = _mem.Entries();
        if (entries.Length == 0) return;

        var path = Path.Combine(_dir, TableName(_nextFileNum++));
        long written;
        try
        {
            // With no older table there is nothing a tombstone could still hide, so it is not written.
            written = TableWriter.Write(path, entries, _opts.BlockSize, _opts.BloomBitsPerKey,
                dropTombstones: _tables.Count == 0, sync);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LevelDbIOException($"leveldb: table write failed for {path}", ex);
        }

        AfterTableWriteBeforeCurrent?.Invoke();

        if (written > 0)
            _tables = _tables.With(Table.Open(path));
        else
            TryDeleteBestEffort(path);

        RotateJournal(); // publishes the new table list
        _mem.Clear();
        ScheduleCompaction();
    }

    // ---- compaction --------------------------------------------------------------------------------------

    /// <summary>Runs compaction to completion on the calling thread. For tests and tools; normal operation needs it never.</summary>
    public void CompactNow()
    {
        EnsureOpen();
        try { _compactionTask?.Wait(); } catch { /* the failed run already stopped */ }
        lock (_writeGate) _compactionRunning = true;
        CompactLoop();
    }

    private void ScheduleCompaction()
    {
        if (!_opts.BackgroundCompaction || _compactionRunning || _closed || _stopCompaction.IsCancellationRequested) return;
        if (PickRun(_tables) is null) return;
        _compactionRunning = true;
        _compactionTask = Task.Run(CompactLoop);
    }

    /// <summary>
    /// Picks neighbouring tables to merge: the newest window of <see cref="Options.CompactionFanIn"/> tables whose
    /// sizes are within 4x of each other (so small fresh tables fold together and big old ones are left alone),
    /// or, when there are too many tables, the smallest window.
    /// </summary>
    private (int Start, int Count)? PickRun(TableSet set)
    {
        var fan = Math.Max(2, _opts.CompactionFanIn);
        if (set.Count < fan) return null;

        for (var start = set.Count - fan; start >= 0; start--)
        {
            long min = long.MaxValue, max = 0;
            for (var i = start; i < start + fan; i++)
            {
                min = Math.Min(min, set[i].FileLength);
                max = Math.Max(max, set[i].FileLength);
            }

            if (max <= 4 * Math.Max(1, min)) return (start, fan);
        }

        if (set.Count <= _opts.MaxTableCount) return null;
        var best = 0;
        var bestTotal = long.MaxValue;
        for (var start = 0; start + fan <= set.Count; start++)
        {
            var total = 0L;
            for (var i = start; i < start + fan; i++) total += set[i].FileLength;
            if (total < bestTotal) { bestTotal = total; best = start; }
        }

        return (best, fan);
    }

    private void CompactLoop()
    {
        try
        {
            while (!_stopCompaction.IsCancellationRequested)
            {
                TableSet set;
                (int Start, int Count) run;
                ulong fileNumber;
                lock (_writeGate)
                {
                    if (_closed) return;
                    set = _tables;
                    if (PickRun(set) is not { } picked) return;
                    run = picked;
                    fileNumber = _nextFileNum++;
                }

                var inputs = Enumerable.Range(run.Start, run.Count).Select(i => set[i]).ToArray();
                var path = Path.Combine(_dir, TableName(fileNumber));
                var dropTombstones = run.Start == 0; // the run holds the oldest table: nothing older to hide
                var newestFirst = inputs.Reverse().Select(t => (ISortedCursor)new TableCursor(t)).ToList();
                var merged = TableWriter.Write(path, MergeEntries(new MergeCursor(newestFirst, skipTombstones: false)),
                    _opts.BlockSize, _opts.BloomBitsPerKey, dropTombstones, sync: true, _stopCompaction.Token);

                var replacement = merged > 0 ? Table.Open(path) : null;
                if (merged == 0) TryDeleteBestEffort(path);

                lock (_writeGate)
                {
                    var current = _tables;
                    var at = -1;
                    for (var i = 0; i < current.Count && at < 0; i++)
                        if (ReferenceEquals(current[i], inputs[0])) at = i;

                    var intact = !_closed && at >= 0 && at + inputs.Length <= current.Count &&
                                 inputs.Select((t, i) => ReferenceEquals(current[at + i], t)).All(same => same);
                    if (!intact)
                    {
                        replacement?.Dispose();
                        TryDeleteBestEffort(path);
                        return;
                    }

                    _tables = current.Replace(at, inputs.Length, replacement);
                    WriteCurrentManifest();
                }

                // Readers still holding the old set keep working through their open handles (FileShare.Delete).
                foreach (var old in inputs) TryDeleteBestEffort(old.Path);
            }
        }
        catch (OperationCanceledException)
        {
            // closing
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Compaction only tidies; a failed run leaves the tables as they were and is retried by the next flush.
            _opts.OnCorruption?.Invoke($"leveldb: compaction stopped: {ex.Message}");
        }
        finally
        {
            lock (_writeGate) _compactionRunning = false;
        }
    }

    private static IEnumerable<KeyValuePair<byte[], byte[]?>> MergeEntries(MergeCursor merge)
    {
        for (merge.SeekToFirst(); merge.Valid; merge.Next())
            yield return new KeyValuePair<byte[], byte[]?>(merge.Key, merge.Value);
    }

    public void Close()
    {
        if (_closed) return;
        _stopCompaction.Cancel();
        try { _compactionTask?.Wait(TimeSpan.FromSeconds(10)); } catch { /* cancelled or failed: nothing to wait for */ }

        lock (_writeGate)
        {
            if (_closed) return;
            try
            {
                if (_mem.Count > 0)
                    FlushMemTableUnlocked(sync: true);
            }
            finally
            {
                _journal?.Dispose();
                _journal = null;
                _lockHandle?.Dispose();
                _lockHandle = null;
                _closed = true;
                foreach (var table in _tables.Oldest) table.Dispose();
            }
        }
    }

    public void Dispose() => Close();

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
    }
}
