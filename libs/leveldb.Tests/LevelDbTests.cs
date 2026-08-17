using System.Collections.Concurrent;
using System.Text;
using Xunit;
using Zenith.LevelDB;

namespace leveldb.Tests;

/// <summary>Test-only convenience wrappers over the zero-alloc <c>TryGet</c> API (ADR §118) — keeps
/// the many existing "get and assert" test lines terse without reintroducing the removed
/// clone-and-return-byte[]? shape into the library itself.</summary>
file static class DbTestExtensions
{
    public static byte[]? Get(this DB db, byte[] key) => db.TryGet(key, out var v) ? v.ToArray() : null;
    public static byte[]? Get(this Snapshot snap, byte[] key) => snap.TryGet(key, out var v) ? v.ToArray() : null;
}

public class LevelDbTests
{
    [Fact]
    public void GetPut_round_trip_persists_across_reopen()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("hello"), Encoding.UTF8.GetBytes("world"));
                db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Equal("world", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("hello"))!));
            Assert.Equal("1", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("a"))!));
            Assert.Null(reopened.Get(Encoding.UTF8.GetBytes("missing")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Iterator_Seek_prefix_scans_overlays()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            db.Put(Encoding.UTF8.GetBytes("c:0:0"), Encoding.UTF8.GetBytes("col"));
            db.Put(Encoding.UTF8.GetBytes("ov:1:2:3"), BitConverter.GetBytes(10));
            db.Put(Encoding.UTF8.GetBytes("ov:1:2:4"), BitConverter.GetBytes(20));
            db.Put(Encoding.UTF8.GetBytes("zz:tail"), Encoding.UTF8.GetBytes("nope"));

            var found = new List<string>();
            using var it = db.CreateIterator();
            it.Seek(Encoding.UTF8.GetBytes("ov:"));
            while (it.IsValid())
            {
                var key = Encoding.UTF8.GetString(it.Key().Span);
                if (!key.StartsWith("ov:", StringComparison.Ordinal))
                    break;
                found.Add(key);
                it.Next();
            }

            Assert.Equal(new[] { "ov:1:2:3", "ov:1:2:4" }, found);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Delete_removes_key_after_flush()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true, WriteBufferSize = 32 }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));
                for (var i = 0; i < 20; i++)
                    db.Put(Encoding.UTF8.GetBytes($"pad{i}"), new byte[64]);
                db.Delete(Encoding.UTF8.GetBytes("k"));
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Null(reopened.Get(Encoding.UTF8.GetBytes("k")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Write_batch_atomic_put_delete_round_trip()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("keep"), Encoding.UTF8.GetBytes("yes"));
                db.Put(Encoding.UTF8.GetBytes("gone"), Encoding.UTF8.GetBytes("no"));

                var batch = new WriteBatch();
                batch.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
                batch.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"));
                batch.Delete(Encoding.UTF8.GetBytes("gone"));
                batch.Put(Encoding.UTF8.GetBytes("keep"), Encoding.UTF8.GetBytes("updated"));
                db.Write(batch);
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Equal("1", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("a"))!));
            Assert.Equal("2", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("b"))!));
            Assert.Equal("updated", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("keep"))!));
            Assert.Null(reopened.Get(Encoding.UTF8.GetBytes("gone")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Write_with_Sync_true_survives_reopen()
    {
        var dir = NewDir();
        try
        {
            var db = new DB(new Options { CreateIfMissing = true }, dir);
            var batch = new WriteBatch();
            batch.Put(Encoding.UTF8.GetBytes("sync"), Encoding.UTF8.GetBytes("durable"));
            batch.Put(Encoding.UTF8.GetBytes("x"), Encoding.UTF8.GetBytes("y"));
            db.Write(batch, new WriteOptions { Sync = true });
            db.Dispose();

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Equal("durable", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("sync"))!));
            Assert.Equal("y", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("x"))!));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Open_recovers_when_orphan_ldb_exists_but_CURRENT_points_at_old()
    {
        var dir = NewDir();
        try
        {
            var keyA = Encoding.UTF8.GetBytes("k");
            var valA = Encoding.UTF8.GetBytes("A");
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(keyA, valA);
            }

            var currentName = File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim();
            Assert.False(string.IsNullOrEmpty(currentName));
            Assert.True(File.Exists(Path.Combine(dir, currentName)));

            // Simulate crash after writing a new snapshot but before CURRENT publish.
            var orphanPath = Path.Combine(dir, "999999.ldb");
            TableFile.Write(
                orphanPath,
                [new KeyValuePair<byte[], byte[]>(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("B"))],
                sync: true);
            Assert.True(File.Exists(orphanPath));
            Assert.Equal(currentName, File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim());

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Equal("A", Encoding.UTF8.GetString(reopened.Get(keyA)!));
            // Orphan must not be source of truth; GC may delete it on Open.
            Assert.False(File.Exists(orphanPath));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Crash_mid_flush_hook_keeps_CURRENT_old_orphan_ignored_WAL_still_recovers()
    {
        // Fault after .ldb write, before CURRENT publish. Orphan is not source of truth;
        // Open still replays WAL (durable Puts before flush) — documented recovery.
        var dir = NewDir();
        var recovery = NewDir();
        DB.AfterTableWriteBeforeCurrent = null;
        try
        {
            var key = Encoding.UTF8.GetBytes("k");
            using (var db = new DB(new Options { CreateIfMissing = true, WriteBufferSize = 32 }, dir))
            {
                db.Put(key, Encoding.UTF8.GetBytes("A"));
                for (var i = 0; i < 8; i++)
                    db.Put(Encoding.UTF8.GetBytes($"pad{i}"), new byte[64]);
            }

            var currentBefore = File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim();
            Assert.False(string.IsNullOrEmpty(currentBefore));

            DB.AfterTableWriteBeforeCurrent = () => throw new IOException("simulated crash mid-flush");
            // Intentionally leaked: Dispose/Close would finish the flush and defeat the crash sim.
            var leaked = new DB(new Options { CreateIfMissing = false, WriteBufferSize = 32 }, dir);
            try
            {
                leaked.Put(key, Encoding.UTF8.GetBytes("B"));
                for (var i = 0; i < 8; i++)
                    leaked.Put(Encoding.UTF8.GetBytes($"more{i}"), new byte[64]);
                Assert.Fail("expected flush to throw via fault hook");
            }
            catch (IOException)
            {
                // Table written, CURRENT not published.
            }
            finally
            {
                DB.AfterTableWriteBeforeCurrent = null;
            }

            Assert.Equal(currentBefore, File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim());
            Assert.True(Directory.EnumerateFiles(dir, "*.ldb").Count() >= 2);

            Directory.CreateDirectory(recovery);
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                try
                {
                    File.Copy(path, Path.Combine(recovery, Path.GetFileName(path)!), overwrite: true);
                }
                catch
                {
                    // Skip files still exclusively locked by the leaked writer handle.
                }
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, recovery);
            // WAL replay brings B; orphan .ldb alone would have been ignored (see orphan_ldb test).
            Assert.Equal("B", Encoding.UTF8.GetString(reopened.Get(key)!));
            var live = File.ReadAllText(Path.Combine(recovery, "CURRENT")).Trim();
            Assert.DoesNotContain(
                Directory.EnumerateFiles(recovery, "*.ldb").Select(Path.GetFileName),
                n => !string.Equals(n, live, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DB.AfterTableWriteBeforeCurrent = null;
            TryDelete(dir);
            TryDelete(recovery);
        }
    }

    [Fact]
    public void Open_fails_clearly_when_CURRENT_missing_and_CreateIfMissing_false()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "LOCK"), "");
            File.WriteAllBytes(Path.Combine(dir, "000001.ldb"), [1, 2, 3]);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new DB(new Options { CreateIfMissing = false }, dir));
            Assert.Contains("CURRENT", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Open_CURRENT_garbage_resets_empty_when_CreateIfMissing_true()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "LOCK"), "");
            File.WriteAllText(Path.Combine(dir, "CURRENT"), "not-a-real-table.ldb\n");

            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            Assert.Null(db.Get(Encoding.UTF8.GetBytes("anything")));
            db.Put(Encoding.UTF8.GetBytes("ok"), Encoding.UTF8.GetBytes("yes"));
            Assert.Equal("yes", Encoding.UTF8.GetString(db.Get(Encoding.UTF8.GetBytes("ok"))!));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Open_truncated_WAL_keeps_complete_records_only()
    {
        // A torn tail (writer died mid-append, header itself incomplete) is a normal crash
        // shape — treated as EOF, not corruption, and never reaches CRC comparison.
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("keep"), Encoding.UTF8.GetBytes("v1"));
            }

            var logs = Directory.GetFiles(dir, "*.log");
            Assert.NotEmpty(logs);
            // Append a torn record header: type + a few bytes, short of the 9-byte header.
            using (var fs = new FileStream(logs.OrderBy(f => f).Last(), FileMode.Append, FileAccess.Write))
            {
                fs.WriteByte(3); // TypeBatch
                fs.Write(BitConverter.GetBytes(100)); // partial crc field
                fs.WriteByte(0xAA); // torn — header needs 9 bytes total, only 6 written
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Equal("v1", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("keep"))!));
            Assert.Null(reopened.Get(Encoding.UTF8.GetBytes("ghost")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Open_WAL_mid_stream_corruption_stops_replay_and_reports_via_OnCorruption()
    {
        // A structurally-complete record (correct header + full body length available) whose CRC
        // doesn't match its bytes is genuine corruption, not a crash-torn tail — everything before
        // it stays trusted, everything from it onward (even later structurally-valid records) is
        // discarded, matching real LevelDB's "don't trust a corrupted record to point at a valid
        // next record" rule.
        var dir = NewDir();
        var recovery = NewDir();
        try
        {
            // Leaked intentionally (same pattern as the mid-flush crash test): Close()/Dispose()
            // would flush this Put to a snapshot and rotate to a fresh empty journal, leaving
            // nothing left in the WAL to corrupt.
            var leaked = new DB(new Options { CreateIfMissing = true }, dir);
            leaked.Put(Encoding.UTF8.GetBytes("before"), Encoding.UTF8.GetBytes("safe"));

            Directory.CreateDirectory(recovery);
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                try
                {
                    File.Copy(path, Path.Combine(recovery, Path.GetFileName(path)!), overwrite: true);
                }
                catch
                {
                    // Skip files still exclusively locked by the leaked writer handle.
                }
            }

            var logPath = Directory.GetFiles(recovery, "*.log").OrderBy(f => f).Last();
            var bytes = File.ReadAllBytes(logPath);
            Assert.True(bytes.Length > 9);
            // Flip one byte inside the first record's body (past the 9-byte header) so the header
            // (and its declared length) still parses fine, but the CRC no longer matches.
            bytes[9] ^= 0xFF;
            File.WriteAllBytes(logPath, bytes);

            var messages = new List<string>();
            using var reopened = new DB(new Options { CreateIfMissing = false, OnCorruption = messages.Add }, recovery);
            Assert.Null(reopened.Get(Encoding.UTF8.GetBytes("before")));
            Assert.Single(messages);
            Assert.Contains("CRC mismatch", messages[0]);
        }
        finally
        {
            TryDelete(dir);
            TryDelete(recovery);
        }
    }

    [Fact]
    public void Open_corrupted_snapshot_throws_instead_of_silently_loading_bad_data()
    {
        // A byte flip inside a live .ldb (magic/version still intact, so it's unambiguously a
        // ZLDB file, not a foreign one) must fail loudly on reopen — silently treating it as
        // "stale/foreign, start empty" (the fallback for a genuinely non-ZLDB file) would discard
        // a corrupted but real snapshot without telling anyone.
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));
            }

            var currentName = File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim();
            var tablePath = Path.Combine(dir, currentName);
            Assert.True(File.Exists(tablePath));

            var bytes = File.ReadAllBytes(tablePath);
            Assert.True(bytes.Length > 4);
            // Flip a byte inside an entry (past the 4-byte magic), leaving magic/version intact.
            bytes[^5] ^= 0xFF;
            File.WriteAllBytes(tablePath, bytes);

            var ex = Assert.Throws<InvalidDataException>(() =>
                new DB(new Options { CreateIfMissing = false }, dir));
            Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Put_after_Close_throws_ObjectDisposed_API_is_lock_serialized_not_concurrent()
    {
        // Documented contract: DB is not multi-thread safe for concurrent Put+Close;
        // operations serialize on an internal gate. Sequential Put after Close must throw.
        var dir = NewDir();
        try
        {
            var db = new DB(new Options { CreateIfMissing = true }, dir);
            db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            db.Close();
            Assert.Throws<ObjectDisposedException>(() =>
                db.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Open_rejects_a_second_instance_on_the_same_directory()
    {
        // Without a real OS-level lock, two DB instances on the same directory would each keep
        // their own independent in-RAM state and silently clobber each other's WAL/snapshot on
        // flush — this is the exact failure mode a real exclusive lock exists to prevent.
        var dir = NewDir();
        try
        {
            using var first = new DB(new Options { CreateIfMissing = true }, dir);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new DB(new Options { CreateIfMissing = false }, dir));
            Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Close_releases_the_lock_so_the_directory_can_be_reopened()
    {
        var dir = NewDir();
        try
        {
            var first = new DB(new Options { CreateIfMissing = true }, dir);
            first.Close();

            using var second = new DB(new Options { CreateIfMissing = false }, dir); // must not throw
            second.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void A_failed_Open_releases_the_lock_instead_of_leaking_it()
    {
        // Regression coverage: an early version of the lock acquired it inside Open() but only
        // released it on a *successful* open — Open() throwing partway through (missing CURRENT
        // with CreateIfMissing=false, here) leaked the lock and permanently locked the directory
        // out of any future attempt.
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "LOCK"), "");
            File.WriteAllBytes(Path.Combine(dir, "000001.ldb"), [1, 2, 3]);

            Assert.Throws<InvalidOperationException>(() =>
                new DB(new Options { CreateIfMissing = false }, dir));

            using var opened = new DB(new Options { CreateIfMissing = true }, dir); // must not throw "locked"
            opened.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Destroy_removes_only_ZLDB_owned_files_and_the_dir_when_empty()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            }

            var stranger = Path.Combine(dir, "not-ours.txt");
            File.WriteAllText(stranger, "leave me alone");

            DB.Destroy(dir);

            Assert.False(File.Exists(Path.Combine(dir, "LOCK")));
            Assert.False(File.Exists(Path.Combine(dir, "CURRENT")));
            Assert.Empty(Directory.GetFiles(dir, "*.log"));
            Assert.Empty(Directory.GetFiles(dir, "*.ldb"));
            // A file Destroy doesn't own keeps the directory itself alive.
            Assert.True(File.Exists(stranger));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Destroy_on_missing_directory_is_a_silent_no_op()
    {
        var dir = NewDir(); // never created
        DB.Destroy(dir); // must not throw
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Repair_discards_a_corrupted_snapshot_and_reopens_cleanly()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("gone-with-snapshot"), Encoding.UTF8.GetBytes("v1"));
            }

            var currentName = File.ReadAllText(Path.Combine(dir, "CURRENT")).Trim();
            var tablePath = Path.Combine(dir, currentName);
            var bytes = File.ReadAllBytes(tablePath);
            bytes[^5] ^= 0xFF; // corrupt the snapshot content, same technique as the load test above
            File.WriteAllBytes(tablePath, bytes);

            // Normal open still fails loudly — repair is opt-in, never automatic.
            Assert.Throws<InvalidDataException>(() => new DB(new Options { CreateIfMissing = false }, dir));

            DB.Repair(dir, new Options());

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            // The corrupted snapshot's only key is gone (no per-entry recovery is possible from a
            // single whole-file checksum — see Repair's doc comment) but the directory is valid
            // and openable again, and new writes work normally.
            Assert.Null(reopened.Get(Encoding.UTF8.GetBytes("gone-with-snapshot")));
            reopened.Put(Encoding.UTF8.GetBytes("after-repair"), Encoding.UTF8.GetBytes("v2"));
            Assert.Equal("v2", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("after-repair"))!));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Repair_on_a_healthy_DB_is_harmless()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));
            }

            DB.Repair(dir, new Options());

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Equal("v", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("k"))!));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void GetSnapshot_view_does_not_see_writes_made_after_it_was_taken()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));

            var snapshot = db.GetSnapshot();
            db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("2"));
            db.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("new"));

            Assert.Equal("1", Encoding.UTF8.GetString(snapshot.Get(Encoding.UTF8.GetBytes("a"))!));
            Assert.Null(snapshot.Get(Encoding.UTF8.GetBytes("b")));

            // Live DB reflects both writes; the snapshot's own iterator still shows the old view.
            Assert.Equal("2", Encoding.UTF8.GetString(db.Get(Encoding.UTF8.GetBytes("a"))!));
            using var it = snapshot.CreateIterator();
            it.SeekToFirst();
            var seen = new List<string>();
            while (it.IsValid())
            {
                seen.Add(Encoding.UTF8.GetString(it.Key().Span));
                it.Next();
            }

            Assert.Equal(new[] { "a" }, seen);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void LevelDbIOException_is_an_IOException_and_preserves_the_original_cause()
    {
        // The wrap keeps existing `catch (IOException)` call sites working unchanged (confirmed
        // by the mid-flush crash test above, which still passes with this type in play) while
        // giving a caller that wants specificity a single type to catch instead of needing to
        // know every raw BCL exception a storage failure could surface as.
        var original = new IOException("disk full");
        var wrapped = new LevelDbIOException("leveldb: snapshot write failed for /some/path", original);

        Assert.IsAssignableFrom<IOException>(wrapped);
        Assert.Same(original, wrapped.InnerException);
        Assert.Contains("snapshot write failed", wrapped.Message);
    }

    [Fact]
    public void Concurrent_reads_and_writes_do_not_corrupt_or_deadlock()
    {
        // Empirical validation of the ADR §118 reader/writer lock model — not just code review.
        // Distinct keys per writer thread so a wrong final count can only mean the MemTable's
        // SortedDictionary was mutated unsafely under concurrent access (it is not thread-safe on
        // its own; correctness here depends entirely on the write lock actually serializing all
        // mutation), not an intentional overwrite race.
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);

            const int writerCount = 4;
            const int readerCount = 8;
            const int writesPerWriter = 250;
            var exceptions = new ConcurrentBag<Exception>();
            using var barrier = new Barrier(writerCount + readerCount);
            using var stopReading = new CancellationTokenSource();

            var writers = Enumerable.Range(0, writerCount).Select(w => Task.Run(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    for (var i = 0; i < writesPerWriter; i++)
                        db.Put(Encoding.UTF8.GetBytes($"w{w}:k{i}"), BitConverter.GetBytes(i));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            var readers = Enumerable.Range(0, readerCount).Select(_1 => Task.Run(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    while (!stopReading.IsCancellationRequested)
                    {
                        using var it = db.CreateIterator();
                        it.SeekToFirst();
                        while (it.IsValid()) it.Next();
                        db.TryGet(Encoding.UTF8.GetBytes("w0:k0"), out _);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            Assert.True(Task.WaitAll(writers, TimeSpan.FromSeconds(30)), "writers did not finish — possible deadlock");
            stopReading.Cancel();
            Assert.True(Task.WaitAll(readers, TimeSpan.FromSeconds(10)), "readers did not finish — possible deadlock");

            Assert.Empty(exceptions);

            using var final = db.CreateIterator();
            final.SeekToFirst();
            var total = 0;
            while (final.IsValid()) { total++; final.Next(); }
            Assert.Equal(writerCount * writesPerWriter, total);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Concurrent_overwrites_of_the_same_key_never_produce_a_torn_read()
    {
        // Fase 8 (ADR §118) made TryGet return the array MemTable already owns instead of cloning
        // it, safe specifically because MemTable never mutates an already-stored array in place.
        // This test empirically stresses exactly that claim: a hot key is overwritten continuously
        // while readers hammer TryGet on it; every value observed must be one complete, internally
        // consistent version — never a mix of an old and a new write's bytes.
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            var key = Encoding.UTF8.GetBytes("hot-key");
            db.Put(key, MakeSelfConsistentValue(0));

            var exceptions = new ConcurrentBag<Exception>();
            using var stop = new CancellationTokenSource();

            var writer = Task.Run(() =>
            {
                try
                {
                    var i = 1;
                    while (!stop.IsCancellationRequested)
                    {
                        db.Put(key, MakeSelfConsistentValue(i));
                        i++;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 500)
                    {
                        if (db.TryGet(key, out var value) && !IsSelfConsistent(value.Span))
                            throw new InvalidOperationException("torn read observed");
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            Assert.True(Task.WaitAll(readers, TimeSpan.FromSeconds(10)), "readers did not finish — possible deadlock");
            stop.Cancel();
            Assert.True(writer.Wait(TimeSpan.FromSeconds(10)), "writer did not finish — possible deadlock");

            Assert.Empty(exceptions);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>16 bytes, every 4-byte word equals <paramref name="n"/> — a torn read mixing bytes
    /// from two different writes would have mismatched words.</summary>
    private static byte[] MakeSelfConsistentValue(int n)
    {
        var v = new byte[16];
        for (var i = 0; i < 4; i++)
            BitConverter.GetBytes(n).CopyTo(v, i * 4);
        return v;
    }

    private static bool IsSelfConsistent(ReadOnlySpan<byte> value)
    {
        if (value.Length != 16) return false;
        var n = BitConverter.ToInt32(value.Slice(0, 4));
        for (var i = 1; i < 4; i++)
        {
            if (BitConverter.ToInt32(value.Slice(i * 4, 4)) != n) return false;
        }

        return true;
    }

    private static string NewDir() =>
        Path.Combine(Path.GetTempPath(), "zenith-leveldb-" + Guid.NewGuid().ToString("N"));

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }
}
