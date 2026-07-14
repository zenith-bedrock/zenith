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
            // Dispose closes + flushes; Sync already fsynced the WAL before return.
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
