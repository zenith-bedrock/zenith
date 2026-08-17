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

    /// <summary>
    /// Write-side exclusion only (ADR §119). Reads (<see cref="TryGet"/>, <see cref="CreateIterator()"/>,
    /// <see cref="GetSnapshot()"/>) take no lock at all — <see cref="MemTable"/> is internally
    /// lock-free (atomic immutable-map swap), so there is nothing for a reader to wait on. This
    /// gate exists purely to serialize <see cref="Write"/>/<see cref="Close"/>/flush, which must
    /// stay single-writer to keep WAL append and memtable mutation atomic together.
    /// <para/>
    /// ADR §118 first tried a <see cref="ReaderWriterLockSlim"/> here on the theory that concurrent
    /// readers would then stop blocking each other. Dedicated benchmarks
    /// (<c>LevelDbLockPrimitiveBenchmarks</c>) showed that was the wrong fix: for ZLDB's actual
    /// workload — many short, cheap point reads — *any* lock (a plain mutex or
    /// <see cref="ReaderWriterLockSlim"/> alike) cost roughly 18-20x throughput at 8 concurrent
    /// readers versus no lock at all; the bookkeeping overhead of acquiring/releasing the lock
    /// dominated, not the work inside it. §119 removes the read-side lock entirely instead of
    /// picking a different lock primitive.
    /// </summary>
    private readonly object _writeGate = new();

    private readonly MemTable _mem = new();
    private JournalWriter? _journal;
    private FileStream? _lockHandle;
    private string? _tablePath;
    private ulong _nextFileNum = 1;
    private long _dirtyBytes;

    /// <summary>Volatile: read by <see cref="EnsureOpen"/> from lock-free readers, written by
    /// <see cref="Close"/> under <see cref="_writeGate"/> — must be visible across threads without
    /// either side taking a lock to observe it.</summary>
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
            // Open() may throw after already acquiring the OS-level lock (e.g. a corrupted
            // snapshot's InvalidDataException, thrown well after the lock is taken) — release it
            // here so a failed open doesn't leave the directory permanently locked out from any
            // future attempt, including a subsequent Repair.
            _journal?.Dispose();
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
    /// Scope, honestly: unlike real LevelDB's <c>RepairDB</c> (which can recover individual valid
    /// blocks/SSTables out of a partially-damaged multi-file store), ZLDB's snapshot is a single
    /// file with one whole-file trailing checksum — there is no finer-grained "this part of the
    /// snapshot is still good" to recover. So repair's snapshot handling is binary: if it fails its
    /// checksum, it is discarded entirely (not partially salvaged), and the DB is rebuilt from
    /// whatever the WAL still has (which itself may only partially replay — everything before a
    /// corrupt point in the WAL is kept, same as normal <see cref="JournalWriter.Replay"/>).
    /// Rewrites a fresh, valid <c>CURRENT</c>/snapshot/journal as a side effect of opening once
    /// tolerantly; the directory is left in a normal, openable state afterward.
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

    private void Open() => Open(tolerateCorruptSnapshot: false);

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

        // A real OS-level exclusive lock (FileShare.None), held for the DB's whole lifetime — not
        // just a marker file. Mirrors real LevelDB's LockFile (POSIX flock/Windows LockFileEx):
        // without this, nothing stopped two DB instances — same process or different processes —
        // from opening the same directory concurrently, each with its own independent _mem/
        // _journal/_nextFileNum, silently clobbering each other's WAL/snapshot on flush. .NET's
        // own file-sharing model already gives mandatory (not just advisory) locking for free, so
        // no P/Invoke is needed the way native LevelDB requires per-OS Env implementations.
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

        var current = Path.Combine(_dir, "CURRENT");
        if (File.Exists(current))
        {
            var name = File.ReadAllText(current).Trim();
            if (name.Length > 0)
            {
                _tablePath = Path.Combine(_dir, name);
                if (!File.Exists(_tablePath))
                {
                    // Two different failure modes hide behind "the file CURRENT names is missing"
                    // (ADR §121): a name that isn't even shaped like one of ZLDB's own tables
                    // (e.g. a leftover NuGet LevelDB "MANIFEST-XXXXXX") means this directory was
                    // never really ours — safe to reset empty, matches the sibling "not a Zenith
                    // ZLDB snapshot" branch below. A name that *is* shaped like our own
                    // `{n:D6}.ldb` naming scheme but is missing is a different, more alarming
                    // signal — that file should exist and doesn't, which looks like real data
                    // loss, not foreign residue. Treated the same as a CRC-mismatched snapshot
                    // (§115): fails loudly by default, tolerated only under `Repair`, which is
                    // exactly the explicit, opt-in salvage path for this class of problem.
                    if (LooksLikeZldbTableName(name) && !tolerateCorruptSnapshot)
                    {
                        throw new InvalidDataException(
                            $"leveldb: CURRENT references {name}, which looks like a ZLDB snapshot " +
                            $"but is missing from disk — refusing to silently start empty: {_dir}");
                    }

                    _tablePath = null;
                }
                else if (!TryLoadTable(_tablePath, tolerateCorruptSnapshot))
                {
                    // Not a Zenith ZLDB snapshot (old native LevelDB residue), or — only under
                    // Repair — a ZLDB snapshot that failed its checksum. Start empty either way;
                    // WAL replay below still recovers whatever the log has.
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

    /// <summary>
    /// <see cref="TableFile.TryLoadInto"/>, optionally demoting a checksum-failure
    /// <see cref="InvalidDataException"/> to a plain "not usable" <c>false</c> instead of letting
    /// it propagate — only when <paramref name="tolerateCorruption"/> is set (i.e. only from
    /// <see cref="Repair"/>). The normal, non-repair open path never tolerates this: a corrupted
    /// snapshot fails loudly there, by design (see <see cref="Repair"/>'s doc comment).
    /// </summary>
    private bool TryLoadTable(string path, bool tolerateCorruption)
    {
        try
        {
            return TableFile.TryLoadInto(path, _mem);
        }
        catch (InvalidDataException) when (tolerateCorruption)
        {
            return false;
        }
    }

    private void ReplayJournals()
    {
        var nums = EnumerateNumberedFiles("*.log").Select(f => f.Num).ToList();
        nums.Sort();
        foreach (var n in nums)
            JournalWriter.Replay(Path.Combine(_dir, JournalName(n)), _mem, _opts.OnCorruption);
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

    /// <summary>True if <paramref name="name"/> has the shape ZLDB itself would have produced via
    /// <see cref="TableName"/> — <c>.ldb</c> extension, purely numeric stem — regardless of digit
    /// count/padding (matches the same lenient parse <see cref="EnumerateNumberedFiles"/> already
    /// uses). Used only to distinguish "this looks like our own missing file" (§121, alarming)
    /// from "this was never our file" (leftover foreign-format residue, harmless).</summary>
    private static bool LooksLikeZldbTableName(string name) =>
        string.Equals(Path.GetExtension(name), ".ldb", StringComparison.OrdinalIgnoreCase) &&
        ulong.TryParse(Path.GetFileNameWithoutExtension(name), out _);

    public bool TryGet(byte[] key, out ReadOnlyMemory<byte> value) => TryGet(ReadOptions.Default, key, out value);

    /// <summary>
    /// Zero-alloc, lock-free read (ADR §118 removed the defensive clone; ADR §119 removed the
    /// lock). Returns the array <see cref="MemTable"/> already owns. Safe without cloning because
    /// <see cref="MemTable.Put"/>/<see cref="MemTable.Delete"/> only ever clone-in on write and
    /// never mutate an already-stored array in place — a later write to the same key swaps in a
    /// brand-new array rather than editing the old one, so any array a caller has already been
    /// handed stays valid and immutable forever. Safe without a lock because <see cref="MemTable"/>
    /// itself is lock-free (an immutable map swapped atomically by reference) — there is nothing
    /// this method needs to wait on. Callers must not mutate the returned memory — that invariant
    /// is exactly what makes skipping the clone safe.
    /// <para/>
    /// The key stays <c>byte[]</c>, not a span: <see cref="MemTable"/>'s
    /// <see cref="System.Collections.Immutable.ImmutableSortedDictionary{TKey,TValue}"/> has no
    /// span-based lookup overload — accepting a span here would force an allocation on the key
    /// side to do the lookup, which would make this method *more* allocating than today, not less.
    /// </summary>
    public bool TryGet(ReadOptions options, byte[] key, out ReadOnlyMemory<byte> value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = options;
        EnsureOpen();
        if (_mem.TryGet(key, out var stored, out var deleted) && !deleted && stored is not null)
        {
            value = stored;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Mirrors real LevelDB's <c>DB::GetProperty</c> — a small set of named introspection values
    /// for ops/diagnostics, not gameplay logic. Returns <c>false</c> for an unrecognized name
    /// (never throws for that). Lock-free: reads <see cref="MemTable"/>'s own lock-free
    /// <see cref="MemTable.Count"/>/<see cref="MemTable.ApproxSize"/>.
    /// <para/>
    /// Recognized properties: <c>"zldb.num-entries"</c> (live key count, decimal string) and
    /// <c>"zldb.approximate-memory-usage"</c> (approximate bytes of key+value data resident in
    /// RAM, decimal string). Real LevelDB's much larger property set
    /// (<c>"leveldb.num-files-at-level&lt;N&gt;"</c>, <c>"leveldb.sstables"</c>, ...) describes
    /// multi-file/leveled storage ZLDB doesn't have — not applicable here, not a gap.
    /// </summary>
    public bool GetProperty(string property, out string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureOpen();
        switch (property)
        {
            case "zldb.num-entries":
                value = _mem.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case "zldb.approximate-memory-usage":
                value = _mem.ApproxSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            _dirtyBytes += encoded.Length;
            if (_dirtyBytes >= _opts.WriteBufferSize)
                FlushSnapshotUnlocked(sync: true);
        }
    }

    public Iterator CreateIterator() => CreateIterator(ReadOptions.Default);

    public Iterator CreateIterator(ReadOptions options) => GetSnapshot(options).CreateIterator();

    public Snapshot GetSnapshot() => GetSnapshot(ReadOptions.Default);

    /// <summary>
    /// Materializes the current dataset into a reusable <see cref="Snapshot"/> — one list copy,
    /// same as <see cref="CreateIterator(ReadOptions)"/> already paid per call. Prefer this over
    /// repeated <see cref="CreateIterator()"/> calls when a caller wants several consistent point
    /// reads (<see cref="Snapshot.TryGet"/>) or iterations against the same view.
    /// <para/>
    /// Lock-free (ADR §119): <see cref="MemTable.LiveEntries"/> reads a single atomically-published
    /// immutable snapshot of the live map, so this needs no external lock to be consistent — a
    /// concurrent write can't be observed half-applied, it either hasn't published yet or has fully
    /// published. Reuses the existing key/value array references rather than cloning their bytes
    /// (ADR §118) — safe under the same copy-on-write discipline <see cref="TryGet"/> relies on:
    /// <see cref="MemTable"/> never mutates an already-stored array in place, only ever swaps in a
    /// new one on write.
    /// <para/>
    /// <see cref="MemTable.LiveEntries"/> already returns entries in <see cref="ByteComparer"/>
    /// order (it iterates the already-sorted
    /// <see cref="System.Collections.Immutable.ImmutableSortedDictionary{TKey,TValue}"/> backing
    /// <see cref="MemTable"/>), so this is a direct O(n) copy — not the O(n log n) it would
    /// cost to re-insert each entry into a fresh <see cref="SortedDictionary{TKey,TValue}"/>, which
    /// is what this method did before ADR §120.
    /// </summary>
    public Snapshot GetSnapshot(ReadOptions options)
    {
        _ = options;
        EnsureOpen();
        return new Snapshot(_mem.LiveEntries());
    }

    /// <summary>
    /// Test hook: invoked after the new .ldb is written and fsynced, before CURRENT is published.
    /// Set from leveldb.Tests only (InternalsVisibleTo). Throw to simulate crash mid-flush.
    /// </summary>
    internal static Action? AfterTableWriteBeforeCurrent;

    /// <summary>
    /// Writes live dict → new .ldb (fsync) → publish CURRENT → rotate WAL.
    /// Mem keeps live keys; tombstones dropped. Orphans deleted best-effort.
    /// Caller must already hold <see cref="_writeGate"/> — this method never acquires it itself,
    /// only ever called from within <see cref="Write"/>/<see cref="Close"/>'s own locked scope.
    /// </summary>
    private void FlushSnapshotUnlocked(bool sync)
    {
        _mem.DropTombstones();
        var live = _mem.LiveEntries();

        var fileNum = _nextFileNum++;
        var newPath = Path.Combine(_dir, TableName(fileNum));
        try
        {
            TableFile.Write(newPath, live, sync);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LevelDbIOException($"leveldb: snapshot write failed for {newPath}", ex);
        }

        AfterTableWriteBeforeCurrent?.Invoke();

        var oldPath = _tablePath;
        _tablePath = newPath;
        try
        {
            WriteCurrentManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LevelDbIOException($"leveldb: CURRENT publish failed for {_dir}", ex);
        }

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
        lock (_writeGate)
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
                _lockHandle?.Dispose();
                _lockHandle = null;
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
