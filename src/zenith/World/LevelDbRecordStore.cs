using Zenith.LevelDB;

namespace Zenith.World;

/// <summary><see cref="IRecordStore"/> over <see cref="DB"/> (ZLDB, ADR §11): one record, one binary key.</summary>
sealed class LevelDbRecordStore : IRecordStore
{
    private static readonly WriteOptions Durable = new() { Sync = true };

    private const int MaxKeyLength = 17;

    private readonly DB _db;

    public LevelDbRecordStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _db = new DB(new Options { CreateIfMissing = true }, directory);
    }

    public byte[]? Get(RecordKey key) => _db.TryGet(key.ToBytes(), out var value) ? value.ToArray() : null;

    public void Write(ReadOnlySpan<KeyValuePair<RecordKey, byte[]>> entries, bool durable)
    {
        if (entries.IsEmpty) return;
        var batch = new WriteBatch();
        Span<byte> keyBuffer = stackalloc byte[MaxKeyLength];
        foreach (var (key, value) in entries)
        {
            key.WriteTo(keyBuffer);
            batch.Put(keyBuffer[..key.EncodedLength], value);
        }

        _db.Write(batch, durable ? Durable : WriteOptions.Default);
    }

    public bool TryGetProperty(string property, out string value) => _db.GetProperty(property, out value);

    public void Dispose() => _db.Dispose();
}
