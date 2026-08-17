using System.Collections.Concurrent;
using System.Text;
using Xunit;
using Zenith.LevelDB;

namespace leveldb.Tests;

/// <summary>
/// Deep-dive pass (ADR §120): edge cases and complex scenarios beyond <see cref="LevelDbTests"/>'s
/// scenario coverage — algorithmic correctness of the O(log n) Seek rewrite, empty/large payloads,
/// same-key churn, tombstone lifecycle, concurrent-iterator/Close safety, and the low-level pieces
/// (<see cref="MemTable"/>, <see cref="ByteComparer"/>, <see cref="Crc32"/>, <see cref="KvFraming"/>)
/// in isolation rather than only through <see cref="DB"/>'s public surface.
/// </summary>
public class LevelDbEdgeCaseTests
{
    // ---------------------------------------------------------------------
    // Seek / binary search correctness (ADR §120 rewrite from linear scan)
    // ---------------------------------------------------------------------

    [Fact]
    public void Seek_finds_exact_match_first_before_middle_and_last_key()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            foreach (var k in new[] { "a", "c", "e", "g", "i" })
                db.Put(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(k));

            var snap = db.GetSnapshot();
            AssertSeekLands(snap, "a", "a");
            AssertSeekLands(snap, "e", "e");
            AssertSeekLands(snap, "i", "i");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Seek_between_two_keys_lands_on_the_next_key()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            foreach (var k in new[] { "a", "c", "e", "g", "i" })
                db.Put(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(k));

            var snap = db.GetSnapshot();
            // "b" doesn't exist — Seek must land on "c" (the next key >= "b").
            AssertSeekLands(snap, "b", "c");
            AssertSeekLands(snap, "d", "e");
            AssertSeekLands(snap, "h", "i");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Seek_before_the_first_key_lands_on_the_first_key()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            foreach (var k in new[] { "m", "n", "o" })
                db.Put(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(k));

            var snap = db.GetSnapshot();
            AssertSeekLands(snap, "a", "m");
            AssertSeekLands(snap, "", "m"); // empty target — same as SeekToFirst
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Seek_past_the_last_key_is_invalid()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            db.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"));

            var snap = db.GetSnapshot();
            using var it = snap.CreateIterator();
            it.Seek(Encoding.UTF8.GetBytes("z"));
            Assert.False(it.IsValid());
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Seek_on_empty_snapshot_is_immediately_invalid()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            var snap = db.GetSnapshot();
            using var it = snap.CreateIterator();
            it.SeekToFirst();
            Assert.False(it.IsValid());
            it.Seek(Encoding.UTF8.GetBytes("anything"));
            Assert.False(it.IsValid());
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Full_iteration_via_Seek_binary_search_matches_full_iteration_via_SeekToFirst()
    {
        // The O(log n) rewrite (ADR §120) must yield the exact same ordering as before — verify
        // against a larger, randomly-ordered-on-insert dataset (SortedDictionary/Immutable
        // dictionary insertion order shouldn't matter; only key order should).
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            var keys = Enumerable.Range(0, 500).Select(i => $"k{i:D5}").ToList();
            var shuffled = keys.OrderBy(_ => Guid.NewGuid()).ToList();
            foreach (var k in shuffled)
                db.Put(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(k));

            var snap = db.GetSnapshot();
            using var it = snap.CreateIterator();
            it.SeekToFirst();
            var seen = new List<string>();
            while (it.IsValid())
            {
                seen.Add(Encoding.UTF8.GetString(it.Key().Span));
                it.Next();
            }

            Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), seen);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void AssertSeekLands(Snapshot snap, string target, string expectedKey)
    {
        using var it = snap.CreateIterator();
        it.Seek(Encoding.UTF8.GetBytes(target));
        Assert.True(it.IsValid());
        Assert.Equal(expectedKey, Encoding.UTF8.GetString(it.Key().Span));
    }

    // ---------------------------------------------------------------------
    // Destroy / Repair refuse when another instance holds the DB open (ADR §120)
    // ---------------------------------------------------------------------

    [Fact]
    public void Destroy_refuses_when_another_instance_has_the_directory_open()
    {
        var dir = NewDir();
        try
        {
            using var open = new DB(new Options { CreateIfMissing = true }, dir);
            open.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));

            var ex = Assert.Throws<InvalidOperationException>(() => DB.Destroy(dir));
            Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);

            // Nothing was deleted — the open instance keeps working normally afterward.
            Assert.True(File.Exists(Path.Combine(dir, "LOCK")));
            Assert.True(File.Exists(Path.Combine(dir, "CURRENT")));
            Assert.Equal("v", Encoding.UTF8.GetString(open.Get(Encoding.UTF8.GetBytes("k"))!));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Destroy_succeeds_once_the_open_instance_closes()
    {
        var dir = NewDir();
        try
        {
            var db = new DB(new Options { CreateIfMissing = true }, dir);
            db.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));
            db.Close();

            DB.Destroy(dir); // must not throw now that the lock is released

            Assert.False(File.Exists(Path.Combine(dir, "LOCK")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Repair_refuses_when_another_instance_has_the_directory_open()
    {
        var dir = NewDir();
        try
        {
            using var open = new DB(new Options { CreateIfMissing = true }, dir);
            open.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));

            var ex = Assert.Throws<InvalidOperationException>(() => DB.Repair(dir, new Options()));
            Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // ---------------------------------------------------------------------
    // Empty / large payloads
    // ---------------------------------------------------------------------

    [Fact]
    public void Empty_key_and_empty_value_round_trip_across_reopen()
    {
        var dir = NewDir();
        try
        {
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                db.Put(ReadOnlySpan<byte>.Empty, Encoding.UTF8.GetBytes("value-for-empty-key"));
                db.Put(Encoding.UTF8.GetBytes("key-for-empty-value"), ReadOnlySpan<byte>.Empty);
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.True(reopened.TryGet([], out var v1));
            Assert.Equal("value-for-empty-key", Encoding.UTF8.GetString(v1.Span));

            Assert.True(reopened.TryGet(Encoding.UTF8.GetBytes("key-for-empty-value"), out var v2));
            Assert.Equal(0, v2.Length);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Large_value_round_trips_through_WAL_and_through_a_flushed_snapshot()
    {
        var dir = NewDir();
        try
        {
            var big = new byte[2 * 1024 * 1024]; // 2MB — exceeds the default 4MB/2 write buffer easily when padded
            new Random(42).NextBytes(big);
            var key = Encoding.UTF8.GetBytes("big");

            using (var db = new DB(new Options { CreateIfMissing = true, WriteBufferSize = 1024 }, dir))
            {
                db.Put(key, big); // WriteBufferSize=1024 forces an immediate flush to a real .ldb
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.True(reopened.TryGet(key, out var value));
            Assert.True(big.AsSpan().SequenceEqual(value.Span));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // ---------------------------------------------------------------------
    // Same-key churn / tombstone lifecycle
    // ---------------------------------------------------------------------

    [Fact]
    public void Same_key_put_delete_churn_survives_crash_and_replay_to_the_last_write()
    {
        var dir = NewDir();
        try
        {
            var key = Encoding.UTF8.GetBytes("churn");
            const int iterations = 49; // last index is 48; 48 % 7 == 6, so the final op is a Put(48).
            using (var db = new DB(new Options { CreateIfMissing = true }, dir))
            {
                for (var i = 0; i < iterations; i++)
                {
                    if (i % 7 == 0)
                        db.Delete(key);
                    else
                        db.Put(key, BitConverter.GetBytes(i));
                }
            }

            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.True(reopened.TryGet(key, out var value));
            Assert.Equal(iterations - 1, BitConverter.ToInt32(value.Span));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void DropTombstones_removes_deleted_keys_from_RAM_and_they_do_not_resurrect()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true, WriteBufferSize = 1 }, dir);
            // WriteBufferSize=1 forces a flush (and therefore DropTombstones) after every write.
            db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            db.Delete(Encoding.UTF8.GetBytes("a")); // triggers its own flush; tombstone dropped after

            Assert.False(db.TryGet(Encoding.UTF8.GetBytes("a"), out _));

            var snap = db.GetSnapshot();
            using var it = snap.CreateIterator();
            it.SeekToFirst();
            Assert.False(it.IsValid()); // nothing live — the tombstone itself isn't a live entry either
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void GetSnapshot_excludes_deleted_keys_even_before_any_flush()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            db.Put(Encoding.UTF8.GetBytes("keep"), Encoding.UTF8.GetBytes("1"));
            db.Put(Encoding.UTF8.GetBytes("gone"), Encoding.UTF8.GetBytes("2"));
            db.Delete(Encoding.UTF8.GetBytes("gone"));

            var snap = db.GetSnapshot();
            Assert.False(snap.TryGet(Encoding.UTF8.GetBytes("gone"), out _));
            Assert.True(snap.TryGet(Encoding.UTF8.GetBytes("keep"), out _));

            using var it = snap.CreateIterator();
            it.SeekToFirst();
            var seen = new List<string>();
            while (it.IsValid())
            {
                seen.Add(Encoding.UTF8.GetString(it.Key().Span));
                it.Next();
            }

            Assert.Equal(["keep"], seen);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // ---------------------------------------------------------------------
    // Concurrent iterators / Close (real threads, not just documented behavior)
    // ---------------------------------------------------------------------

    [Fact]
    public void Multiple_iterators_over_the_same_snapshot_are_safe_used_concurrently()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            for (var i = 0; i < 200; i++)
                db.Put(Encoding.UTF8.GetBytes($"k{i:D4}"), BitConverter.GetBytes(i));

            var snap = db.GetSnapshot();
            var exceptions = new ConcurrentBag<Exception>();
            var results = new ConcurrentBag<int>();

            var tasks = Enumerable.Range(0, 8).Select(_2 => Task.Run(() =>
            {
                try
                {
                    using var it = snap.CreateIterator();
                    it.SeekToFirst();
                    var count = 0;
                    while (it.IsValid())
                    {
                        count++;
                        it.Next();
                    }

                    results.Add(count);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(10)));
            Assert.Empty(exceptions);
            Assert.All(results, count => Assert.Equal(200, count));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Concurrent_Close_calls_from_multiple_threads_are_idempotent_and_safe()
    {
        var dir = NewDir();
        try
        {
            var db = new DB(new Options { CreateIfMissing = true }, dir);
            db.Put(Encoding.UTF8.GetBytes("k"), Encoding.UTF8.GetBytes("v"));

            var exceptions = new ConcurrentBag<Exception>();
            var tasks = Enumerable.Range(0, 8).Select(_3 => Task.Run(() =>
            {
                try
                {
                    db.Close();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(10)), "Close() calls did not finish — possible deadlock");
            Assert.Empty(exceptions);

            // The lock is released exactly once — a fresh DB can reopen the directory.
            using var reopened = new DB(new Options { CreateIfMissing = false }, dir);
            Assert.Equal("v", Encoding.UTF8.GetString(reopened.Get(Encoding.UTF8.GetBytes("k"))!));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // ---------------------------------------------------------------------
    // WriteBatch same-key multiple ops (documented last-write-wins semantics)
    // ---------------------------------------------------------------------

    [Fact]
    public void WriteBatch_applies_multiple_ops_on_the_same_key_in_order_last_wins()
    {
        // Mirrors WriteBatch's own class doc example: Put "v1", Delete, Put "v2", Put "v3" -> "v3".
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            var key = Encoding.UTF8.GetBytes("key");

            var batch = new WriteBatch();
            batch.Put(key, Encoding.UTF8.GetBytes("v1"));
            batch.Delete(key);
            batch.Put(key, Encoding.UTF8.GetBytes("v2"));
            batch.Put(key, Encoding.UTF8.GetBytes("v3"));
            db.Write(batch);

            Assert.True(db.TryGet(key, out var value));
            Assert.Equal("v3", Encoding.UTF8.GetString(value.Span));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void WriteBatch_Clear_discards_buffered_ops_before_Write()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            var batch = new WriteBatch();
            batch.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            batch.Clear();
            batch.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"));
            Assert.Equal(1, batch.Count);
            db.Write(batch);

            Assert.False(db.TryGet(Encoding.UTF8.GetBytes("a"), out _));
            Assert.True(db.TryGet(Encoding.UTF8.GetBytes("b"), out var value));
            Assert.Equal("2", Encoding.UTF8.GetString(value.Span));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // ---------------------------------------------------------------------
    // MemTable in isolation (internal, InternalsVisibleTo)
    // ---------------------------------------------------------------------

    [Fact]
    public void MemTable_Put_Delete_TryGet_and_ApproxSize_are_consistent()
    {
        var mem = new MemTable();
        Assert.Equal(0, mem.Count);
        Assert.Equal(0, mem.ApproxSize);

        mem.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
        Assert.Equal(1, mem.Count);
        Assert.True(mem.ApproxSize > 0);

        Assert.True(mem.TryGet(Encoding.UTF8.GetBytes("a"), out var v, out var deleted));
        Assert.False(deleted);
        Assert.Equal("1", Encoding.UTF8.GetString(v!));

        mem.Delete(Encoding.UTF8.GetBytes("a"));
        Assert.True(mem.TryGet(Encoding.UTF8.GetBytes("a"), out _, out var deletedAfter));
        Assert.True(deletedAfter); // tombstone: known, but marked deleted

        Assert.False(mem.TryGet(Encoding.UTF8.GetBytes("missing"), out _, out _));
    }

    [Fact]
    public void MemTable_overwriting_a_key_replaces_rather_than_accumulates()
    {
        var mem = new MemTable();
        mem.Put(Encoding.UTF8.GetBytes("a"), new byte[100]);
        var afterFirst = mem.ApproxSize;
        mem.Put(Encoding.UTF8.GetBytes("a"), new byte[10]);
        Assert.Equal(1, mem.Count);
        Assert.True(mem.ApproxSize < afterFirst);
    }

    [Fact]
    public void MemTable_DropTombstones_removes_tombstones_and_shrinks_Count()
    {
        var mem = new MemTable();
        mem.Put(Encoding.UTF8.GetBytes("live"), Encoding.UTF8.GetBytes("1"));
        mem.Put(Encoding.UTF8.GetBytes("dead"), Encoding.UTF8.GetBytes("2"));
        mem.Delete(Encoding.UTF8.GetBytes("dead"));
        Assert.Equal(2, mem.Count); // tombstone still occupies a slot until dropped

        mem.DropTombstones();
        Assert.Equal(1, mem.Count);
        Assert.False(mem.TryGet(Encoding.UTF8.GetBytes("dead"), out _, out _));
        Assert.True(mem.TryGet(Encoding.UTF8.GetBytes("live"), out _, out var deleted));
        Assert.False(deleted);
    }

    [Fact]
    public void MemTable_DropTombstones_on_a_table_with_no_tombstones_is_a_harmless_no_op()
    {
        var mem = new MemTable();
        mem.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
        var before = mem.LiveEntries();
        mem.DropTombstones();
        var after = mem.LiveEntries();
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(1, mem.Count);
    }

    [Fact]
    public void MemTable_Clear_empties_everything()
    {
        var mem = new MemTable();
        mem.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
        mem.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"));
        mem.Clear();
        Assert.Equal(0, mem.Count);
        Assert.Equal(0, mem.ApproxSize);
        Assert.Empty(mem.LiveEntries());
    }

    [Fact]
    public void MemTable_LiveEntries_are_sorted_by_ByteComparer_regardless_of_insertion_order()
    {
        var mem = new MemTable();
        foreach (var k in new[] { "z", "a", "m", "b", "y" })
            mem.Put(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(k));

        var keys = mem.LiveEntries().Select(e => Encoding.UTF8.GetString(e.Key)).ToList();
        Assert.Equal(["a", "b", "m", "y", "z"], keys);
    }

    // ---------------------------------------------------------------------
    // ByteComparer edge cases
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "a", -1)]
    [InlineData("a", "", 1)]
    [InlineData("a", "a", 0)]
    [InlineData("a", "b", -1)]
    [InlineData("b", "a", 1)]
    [InlineData("ab", "a", 1)] // longer, shares prefix -> sorts after
    [InlineData("a", "ab", -1)]
    [InlineData("ab", "ac", -1)]
    public void ByteComparer_matches_expected_sign(string a, string b, int expectedSign)
    {
        var result = ByteComparer.Compare(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Fact]
    public void ByteComparer_instance_treats_null_as_less_than_anything_and_equal_to_null()
    {
        var cmp = (IComparer<byte[]>)ByteComparer.Instance;
        Assert.Equal(0, cmp.Compare(null, null));
        Assert.True(cmp.Compare(null, Encoding.UTF8.GetBytes("a")) < 0);
        Assert.True(cmp.Compare(Encoding.UTF8.GetBytes("a"), null) > 0);
    }

    [Fact]
    public void ByteComparer_handles_bytes_above_0x7F_as_unsigned()
    {
        // 0x80 must sort AFTER 0x7F under unsigned comparison (it would sort before under a
        // naive signed sbyte comparison, since 0x80 as sbyte is negative).
        var result = ByteComparer.Compare([0x7F], [0x80]);
        Assert.True(result < 0);
    }

    // ---------------------------------------------------------------------
    // Crc32 — known test vector + basic properties
    // ---------------------------------------------------------------------

    [Fact]
    public void Crc32_matches_the_well_known_check_value_for_ASCII_digits_123456789()
    {
        // Standard CRC-32/ISO-HDLC (poly 0xEDB88320, the polynomial Crc32.cs itself documents
        // using) check value for the ASCII bytes "123456789" is the widely published 0xCBF43926 —
        // same value zlib's crc32(), PKZIP, and .NET's System.IO.Hashing.Crc32 all produce.
        var bytes = Encoding.ASCII.GetBytes("123456789");
        var crc = Crc32.Compute(bytes);
        Assert.Equal(0xCBF43926u, crc);
    }

    [Fact]
    public void Crc32_of_empty_data_is_zero()
    {
        Assert.Equal(0u, Crc32.Compute([]));
    }

    [Fact]
    public void Crc32_prefix_overload_matches_manually_concatenated_bytes()
    {
        byte[] body = [1, 2, 3, 4, 5];
        var viaPrefix = Crc32.Compute(0xAB, body);

        var combined = new byte[body.Length + 1];
        combined[0] = 0xAB;
        body.CopyTo(combined, 1);
        var viaConcat = Crc32.Compute(combined);

        Assert.Equal(viaConcat, viaPrefix);
    }

    [Fact]
    public void Crc32_detects_a_single_bit_flip()
    {
        byte[] original = Encoding.UTF8.GetBytes("the quick brown fox");
        var flipped = (byte[])original.Clone();
        flipped[3] ^= 0x01;

        Assert.NotEqual(Crc32.Compute(original), Crc32.Compute(flipped));
    }

    // ---------------------------------------------------------------------
    // KvFraming edge cases
    // ---------------------------------------------------------------------

    [Fact]
    public void KvFraming_zero_length_data_round_trips_through_a_stream()
    {
        using var ms = new MemoryStream();
        KvFraming.WriteLengthPrefixed(ms, []);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.True(KvFraming.TryReadLengthPrefixed(reader, ms.Length, out var data));
        Assert.Empty(data);
    }

    [Fact]
    public void KvFraming_TryReadLengthPrefixed_rejects_a_negative_length_field()
    {
        // Hand-craft a malformed length prefix (-1 as int32 LE) to confirm the bounds check
        // catches it rather than throwing an unhandled exception or reading garbage.
        using var ms = new MemoryStream(BitConverter.GetBytes(-1));
        using var reader = new BinaryReader(ms);
        Assert.False(KvFraming.TryReadLengthPrefixed(reader, ms.Length, out var data));
        Assert.Empty(data);
    }

    [Fact]
    public void KvFraming_TryReadLengthPrefixed_span_overload_rejects_length_exceeding_remaining_bytes()
    {
        Span<byte> lenBuf = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lenBuf, 100); // claims 100 bytes
        var source = new byte[4 + 3]; // only 3 actually follow
        lenBuf.CopyTo(source);
        var pos = 0;
        Assert.False(KvFraming.TryReadLengthPrefixed(source, ref pos, out var data));
        Assert.True(data.IsEmpty);
    }

    // ---------------------------------------------------------------------
    // CURRENT referencing a missing table (ADR §121)
    // ---------------------------------------------------------------------

    [Fact]
    public void Open_throws_when_CURRENT_names_a_ZLDB_shaped_table_file_that_is_missing()
    {
        // Distinguishes "this looks like our own missing file" (alarming — treated as corruption,
        // same as a CRC-mismatched snapshot) from "this was never our file" (leftover foreign
        // residue, harmless — see the next test).
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "LOCK"), "");
            File.WriteAllText(Path.Combine(dir, "CURRENT"), "000042.ldb\n"); // never actually written

            var ex = Assert.Throws<InvalidDataException>(() =>
                new DB(new Options { CreateIfMissing = false }, dir));
            Assert.Contains("000042.ldb", ex.Message);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Open_still_resets_empty_for_a_non_ZLDB_shaped_missing_CURRENT_target()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "LOCK"), "");
            // Not numeric-stemmed — looks like foreign (e.g. native LevelDB MANIFEST) residue, not
            // a missing ZLDB table.
            File.WriteAllText(Path.Combine(dir, "CURRENT"), "MANIFEST-000001\n");

            using var db = new DB(new Options { CreateIfMissing = true }, dir); // must not throw
            Assert.False(db.TryGet(Encoding.UTF8.GetBytes("anything"), out _));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Repair_tolerates_a_missing_ZLDB_shaped_CURRENT_target_and_rebuilds_from_WAL()
    {
        var dir = NewDir();
        var recovery = NewDir();
        try
        {
            var key = Encoding.UTF8.GetBytes("safe-in-wal");
            // Leaked intentionally (same pattern as the mid-flush/mid-stream corruption tests in
            // LevelDbTests.cs): Close()/Dispose() would flush this Put to a real snapshot, leaving
            // nothing in the WAL to reproduce "CURRENT references a missing table while the WAL
            // still has the data."
            var leaked = new DB(new Options { CreateIfMissing = true }, dir);
            leaked.Put(key, Encoding.UTF8.GetBytes("v1"));

            Directory.CreateDirectory(recovery);
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                try
                {
                    File.Copy(path, Path.Combine(recovery, Path.GetFileName(path)!), overwrite: true);
                }
                catch
                {
                    // Skip files still exclusively locked by the leaked writer handle (LOCK itself).
                }
            }

            // Point the copy's CURRENT at a ZLDB-shaped table that was never actually written.
            File.WriteAllText(Path.Combine(recovery, "CURRENT"), "000999.ldb\n");

            Assert.Throws<InvalidDataException>(() => new DB(new Options { CreateIfMissing = false }, recovery));

            DB.Repair(recovery, new Options());

            using var reopened = new DB(new Options { CreateIfMissing = false }, recovery);
            Assert.True(reopened.TryGet(key, out var value));
            Assert.Equal("v1", Encoding.UTF8.GetString(value.Span));
        }
        finally
        {
            TryDelete(dir);
            TryDelete(recovery);
        }
    }

    // ---------------------------------------------------------------------
    // GetProperty / WriteBatch.ApproximateSize (ADR §121)
    // ---------------------------------------------------------------------

    [Fact]
    public void GetProperty_reports_entry_count_and_approximate_memory_usage()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            Assert.True(db.GetProperty("zldb.num-entries", out var countBefore));
            Assert.Equal("0", countBefore);

            db.Put(Encoding.UTF8.GetBytes("a"), Encoding.UTF8.GetBytes("1"));
            db.Put(Encoding.UTF8.GetBytes("b"), Encoding.UTF8.GetBytes("2"));

            Assert.True(db.GetProperty("zldb.num-entries", out var count));
            Assert.Equal("2", count);

            Assert.True(db.GetProperty("zldb.approximate-memory-usage", out var mem));
            Assert.True(int.Parse(mem) > 0);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void GetProperty_returns_false_for_an_unrecognized_name()
    {
        var dir = NewDir();
        try
        {
            using var db = new DB(new Options { CreateIfMissing = true }, dir);
            Assert.False(db.GetProperty("leveldb.stats", out var value));
            Assert.Equal("", value);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void WriteBatch_ApproximateSize_grows_with_ops_and_resets_on_Clear()
    {
        var batch = new WriteBatch();
        Assert.Equal(0, batch.ApproximateSize);

        batch.Put(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("value"));
        var afterOnePut = batch.ApproximateSize;
        Assert.True(afterOnePut > 0);

        batch.Delete(Encoding.UTF8.GetBytes("key2"));
        Assert.True(batch.ApproximateSize > afterOnePut);

        batch.Clear();
        Assert.Equal(0, batch.ApproximateSize);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string NewDir() =>
        Path.Combine(Path.GetTempPath(), "zenith-leveldb-edge-" + Guid.NewGuid().ToString("N"));

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

/// <summary>Test-only convenience wrappers over the zero-alloc <c>TryGet</c> API — see
/// <see cref="LevelDbTests"/>'s own copy of this pattern (kept file-local, not shared, since the
/// two test files are otherwise independent).</summary>
file static class DbEdgeCaseExtensions
{
    public static byte[]? Get(this DB db, byte[] key) => db.TryGet(key, out var v) ? v.ToArray() : null;
}
