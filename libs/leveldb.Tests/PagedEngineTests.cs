using System.Text;
using Xunit;
using Zenith.LevelDB;

namespace leveldb.Tests;

/// <summary>ZLDB v2 (ADR §168): memtable flushed to tables, reads across tables, compaction, checksums.</summary>
public class PagedEngineTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "zenith-paged-" + Guid.NewGuid().ToString("N"));

    private static byte[] K(int i) => Encoding.UTF8.GetBytes($"key{i:D6}");

    private static byte[] V(int i, int size = 200)
    {
        var bytes = new byte[size];
        Array.Fill(bytes, (byte)(i % 251));
        BitConverter.GetBytes(i).CopyTo(bytes, 0);
        return bytes;
    }

    private static Options Small(bool compaction = false) => new()
    {
        CreateIfMissing = true,
        WriteBufferSize = 8 * 1024,
        BlockSize = 1024,
        BackgroundCompaction = compaction,
    };

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* handles of deleted tables close on GC */ }
    }

    [Fact]
    public void A_database_much_larger_than_the_write_buffer_reads_back_from_its_tables()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(Small(), dir);
            for (var i = 0; i < 2000; i++) db.Put(K(i), V(i));

            Assert.True(db.GetProperty("zldb.num-tables", out var tables));
            Assert.True(int.Parse(tables, System.Globalization.CultureInfo.InvariantCulture) > 10, "the data should have been flushed to many tables");
            for (var i = 0; i < 2000; i += 37)
            {
                Assert.True(db.TryGet(K(i), out var value));
                Assert.Equal(V(i), value.ToArray());
            }

            Assert.False(db.TryGet(K(99999), out _));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Resident_memory_stays_far_below_the_data_size()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(Small(), dir);
            for (var i = 0; i < 4000; i++) db.Put(K(i), V(i, 500)); // ~2 MB of values

            Assert.True(db.GetProperty("zldb.approximate-memory-usage", out var resident));
            Assert.True(long.Parse(resident, System.Globalization.CultureInfo.InvariantCulture) < 400_000, $"resident {resident} bytes for ~2 MB of data");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_flush_writes_only_the_memtable_never_the_whole_database()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(Small(), dir);
            for (var i = 0; i < 3000; i++) db.Put(K(i), V(i));

            var sizes = Directory.GetFiles(dir, "*.ldb").Select(p => new FileInfo(p).Length).ToList();
            var total = sizes.Sum();
            Assert.True(sizes.Max() < total / 5, "no single table should hold most of the data");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Data_survives_a_reopen_through_tables_and_the_log()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(Small(), dir))
                for (var i = 0; i < 1500; i++) db.Put(K(i), V(i));

            using var reopened = new DB(Small(), dir);
            for (var i = 0; i < 1500; i += 13)
            {
                Assert.True(reopened.TryGet(K(i), out var value));
                Assert.Equal(V(i), value.ToArray());
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_delete_in_a_newer_table_hides_the_key_in_an_older_one()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(Small(), dir);
            db.Put(K(1), V(1));
            for (var i = 100; i < 300; i++) db.Put(K(i), V(i)); // pushes key 1 into a table
            db.Delete(K(1));
            for (var i = 300; i < 500; i++) db.Put(K(i), V(i)); // pushes the tombstone into a newer table

            Assert.False(db.TryGet(K(1), out _));
            using var iterator = db.CreateIterator();
            iterator.SeekToFirst();
            Assert.True(ByteComparer.Compare(iterator.Key().Span, K(100)) == 0, "the deleted key is skipped by iteration");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void The_iterator_merges_memtable_and_tables_in_key_order_with_the_newest_value()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(Small(), dir);
            for (var i = 0; i < 600; i++) db.Put(K(i), V(i));
            db.Put(K(10), V(1010)); // overwrite, still in the memtable

            using var iterator = db.CreateIterator();
            var seen = 0;
            byte[]? previous = null;
            for (iterator.SeekToFirst(); iterator.IsValid(); iterator.Next())
            {
                var key = iterator.Key().ToArray();
                if (previous is not null) Assert.True(ByteComparer.Compare(previous, key) < 0);
                previous = key;
                if (ByteComparer.Compare(key, K(10)) == 0)
                    Assert.Equal(V(1010), iterator.Value().ToArray());
                seen++;
            }

            Assert.Equal(600, seen);

            iterator.Seek(K(300));
            Assert.Equal(K(300), iterator.Key().ToArray());
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Compaction_merges_tables_keeps_every_live_key_and_drops_tombstones()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(Small(), dir);
            for (var i = 0; i < 1500; i++) db.Put(K(i), V(i));
            for (var i = 0; i < 1500; i += 2) db.Delete(K(i));
            for (var i = 1500; i < 2000; i++) db.Put(K(i), V(i));
            db.GetProperty("zldb.num-tables", out var before);

            db.CompactNow();

            db.GetProperty("zldb.num-tables", out var after);
            Assert.True(int.Parse(after, System.Globalization.CultureInfo.InvariantCulture) < int.Parse(before, System.Globalization.CultureInfo.InvariantCulture), $"{before} tables before, {after} after");
            for (var i = 0; i < 2000; i++)
            {
                var expected = i < 1500 && i % 2 == 0 ? null : V(i);
                Assert.Equal(expected is not null, db.TryGet(K(i), out var value));
                if (expected is not null) Assert.Equal(expected, value.ToArray());
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Background_compaction_keeps_reads_correct_while_writes_continue()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(Small(compaction: true), dir))
            {
                for (var i = 0; i < 3000; i++)
                {
                    db.Put(K(i % 700), V(i));
                    if (i % 50 == 0) Assert.True(db.TryGet(K(i % 700), out _));
                }
            }

            using var reopened = new DB(Small(), dir);
            for (var key = 0; key < 700; key++)
            {
                var lastWrite = key + 700 * ((2999 - key) / 700);
                Assert.True(reopened.TryGet(K(key), out var value));
                Assert.Equal(V(lastWrite), value.ToArray());
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_flipped_byte_in_a_data_block_is_reported_on_read_not_returned_as_data()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(Small(), dir))
                for (var i = 0; i < 100; i++) db.Put(K(i), V(i));

            var table = Directory.GetFiles(dir, "*.ldb").OrderBy(p => p).First();
            var bytes = File.ReadAllBytes(table);
            bytes[10] ^= 0xFF; // inside the first data block
            File.WriteAllBytes(table, bytes);

            using var db2 = new DB(Small(), dir);
            var firstKeyOfTable = false;
            Assert.Throws<InvalidDataException>(() =>
            {
                for (var i = 0; i < 100; i++)
                    firstKeyOfTable |= db2.TryGet(K(i), out _);
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void A_flipped_byte_in_the_footer_fails_the_open()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(Small(), dir))
                for (var i = 0; i < 100; i++) db.Put(K(i), V(i));

            var table = Directory.GetFiles(dir, "*.ldb").OrderBy(p => p).First();
            var bytes = File.ReadAllBytes(table);
            bytes[^10] ^= 0xFF;
            File.WriteAllBytes(table, bytes);

            Assert.Throws<InvalidDataException>(() => new DB(Small(), dir));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Bloom_filter_has_no_false_negatives_and_few_false_positives()
    {
        var keys = Enumerable.Range(0, 5000).Select(i => BloomFilter.Hash(K(i))).ToList();
        var bloom = new BloomFilter(BloomFilter.Build(keys, bitsPerKey: 10));

        Assert.All(keys, hash => Assert.True(bloom.MayContain(hash)));
        var falsePositives = Enumerable.Range(100_000, 5000).Count(i => bloom.MayContain(BloomFilter.Hash(K(i))));
        Assert.True(falsePositives < 150, $"{falsePositives} false positives in 5000");
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(300u)]
    [InlineData(uint.MaxValue)]
    public void Varint_round_trips(uint value)
    {
        Span<byte> buffer = stackalloc byte[Varint.MaxLength];
        var length = Varint.Write(buffer, value);
        var position = 0;
        Assert.True(Varint.TryRead(buffer[..length], ref position, out var read));
        Assert.Equal(value, read);
        Assert.Equal(length, position);
    }
}
