using System.Text;
using Xunit;
using Zenith.LevelDB;

namespace leveldb.Tests;

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
                var key = Encoding.UTF8.GetString(it.Key()!);
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
    public void Open_truncated_WAL_keeps_complete_records_only_no_CRC_yet()
    {
        // Limit documented: Journal has no CRC; torn suffix is skipped at replay bounds checks.
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(Encoding.UTF8.GetBytes("keep"), Encoding.UTF8.GetBytes("v1"));
            }

            var logs = Directory.GetFiles(dir, "*.log");
            Assert.NotEmpty(logs);
            // Append a torn batch header: type=3 + incomplete length/payload
            using (var fs = new FileStream(logs.OrderBy(f => f).Last(), FileMode.Append, FileAccess.Write))
            {
                fs.WriteByte(3); // TypeBatch
                fs.Write(BitConverter.GetBytes(100)); // claims 100 bytes
                fs.WriteByte(0xAA); // only 1 byte of payload — torn
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
