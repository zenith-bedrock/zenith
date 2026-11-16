using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public sealed class RecordKeyTests
{
    [Fact]
    public void Column_keys_sort_in_world_order()
    {
        var coords = new[] { (-5, 3), (-1, 0), (0, -2), (0, 0), (0, 7), (1, -9), (300, 0) };
        var keys = coords.Select(c => RecordKey.Column(new ChunkCoord(c.Item1, c.Item2)).ToBytes()).ToList();

        var sorted = keys.OrderBy(k => k, Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b))).ToList();

        Assert.Equal(keys, sorted);
    }

    [Fact]
    public void Kinds_never_share_a_key()
    {
        var uuid = Guid.NewGuid();
        var keys = new[]
        {
            RecordKey.Player(RecordKind.Inventory, uuid).ToBytes(),
            RecordKey.Player(RecordKind.Armor, uuid).ToBytes(),
            RecordKey.Player(RecordKind.Offhand, uuid).ToBytes(),
            RecordKey.Player(RecordKind.PlayerData, uuid).ToBytes(),
            RecordKey.Column(new ChunkCoord(0, 0)).ToBytes(),
            RecordKey.Singleton(RecordKind.WorldMetadata).ToBytes(),
            RecordKey.Singleton(RecordKind.GameRules).ToBytes(),
        };

        Assert.Equal(keys.Length, keys.Select(Convert.ToHexString).Distinct().Count());
    }

    [Fact]
    public void A_key_is_encoded_into_exactly_its_declared_length()
    {
        Assert.Equal(9, RecordKey.Column(new ChunkCoord(1, 2)).ToBytes().Length);
        Assert.Equal(17, RecordKey.Player(RecordKind.Armor, Guid.NewGuid()).ToBytes().Length);
        Assert.Single(RecordKey.Singleton(RecordKind.GameRules).ToBytes());
    }
}

public sealed class WriteBehindStorageTests
{
    private sealed class CountingStore : IRecordStore
    {
        private readonly Dictionary<RecordKey, byte[]> _data = [];
        public int WriteCalls;
        public int RecordsWritten;

        public byte[]? Get(RecordKey key)
        {
            lock (_data) return _data.GetValueOrDefault(key);
        }

        public void Write(ReadOnlySpan<KeyValuePair<RecordKey, byte[]>> entries, bool durable)
        {
            lock (_data)
            {
                WriteCalls++;
                foreach (var (key, value) in entries)
                {
                    _data[key] = value;
                    RecordsWritten++;
                }
            }
        }

        public bool TryGetProperty(string property, out string value)
        {
            value = "";
            return false;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task A_read_sees_a_write_that_has_not_reached_the_store()
    {
        var store = new CountingStore();
        using var storage = new WriteBehindStorage(store);
        var key = RecordKey.Column(new ChunkCoord(4, 5));

        await storage.PutAsync(key, [1, 2, 3]);

        Assert.Equal(new byte[] { 1, 2, 3 }, await storage.GetAsync(key));
    }

    [Fact]
    public async Task Flush_writes_everything_and_keeps_only_the_newest_value_of_a_key()
    {
        var store = new CountingStore();
        using var storage = new WriteBehindStorage(store);
        var key = RecordKey.Column(new ChunkCoord(0, 0));

        for (byte i = 1; i <= 50; i++)
            await storage.PutAsync(key, [i]);
        await storage.FlushAsync();

        Assert.Equal(new byte[] { 50 }, store.Get(key));
        Assert.True(store.RecordsWritten < 50, "writes to one key between drains are coalesced");
    }

    [Fact]
    public async Task Several_records_reach_the_store_as_one_batch()
    {
        var store = new CountingStore();
        using var storage = new WriteBehindStorage(store);
        for (var x = 0; x < 20; x++)
            await storage.PutAsync(RecordKey.Column(new ChunkCoord(x, 0)), [(byte)x]);

        await storage.FlushAsync();

        Assert.Equal(20, store.RecordsWritten);
        Assert.True(store.WriteCalls <= 3, $"{store.WriteCalls} write calls for 20 records");
    }

    [Fact]
    public async Task A_record_never_written_reads_as_null()
    {
        using var storage = new WriteBehindStorage(new CountingStore());
        Assert.Null(await storage.GetAsync(RecordKey.Singleton(RecordKind.GameRules)));
    }

    [Fact]
    public async Task Disposing_drains_what_is_pending()
    {
        var store = new CountingStore();
        var storage = new WriteBehindStorage(store);
        var key = RecordKey.Singleton(RecordKind.GameRules);
        await storage.PutAsync(key, [9]);

        storage.Dispose();

        Assert.Equal(new byte[] { 9 }, store.Get(key));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await storage.PutAsync(key, [1]));
    }
}
