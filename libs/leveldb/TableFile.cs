using System.Buffers.Binary;

namespace Zenith.LevelDB;

/// <summary>
/// Snapshot on-disk ordenado (formato Zenith ZLDB — não compatível com LevelDB.Standard/NuGet).
/// Layout: magic "ZLDB" | version u32=1 | count u32 | entries: i32 klen | key | u32 vlen | value
/// | crc:u32 LE (trailer, over every byte before it — magic through the last entry).
/// Tombstones não são gravados no snapshot compactado (só live keys).
/// Key length shares <see cref="KvFraming"/>'s int32 convention with Journal/WriteBatch. Value
/// length is deliberately its own uint32 (not KvFraming) because <c>uint.MaxValue</c> doubles as
/// a tombstone sentinel here (see TryLoadInto) - a real format difference, not an oversight.
/// The whole snapshot is built in RAM before writing (same "entire dataset fits in RAM" constraint
/// the rest of ZLDB already requires — see README), which makes a single trailing checksum over
/// the fully-materialized content straightforward, unlike real LevelDB's per-block trailers (which
/// exist to avoid buffering an entire multi-file SSTable — not a concern ZLDB has).
/// </summary>
static class TableFile
{
    private static readonly byte[] Magic = "ZLDB"u8.ToArray();
    private const uint Version = 1;
    private const int CrcTrailerSize = sizeof(uint);

    public static void Write(string path, IEnumerable<KeyValuePair<byte[], byte[]>> liveEntries, bool sync)
    {
        var list = new List<KeyValuePair<byte[], byte[]>>(liveEntries);
        list.Sort((a, b) => ByteComparer.Instance.Compare(a.Key, b.Key));

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(list.Count);
            foreach (var (key, value) in list)
            {
                writer.Write(key.Length);
                writer.Write(key);
                writer.Write((uint)value.Length);
                writer.Write(value);
            }
        }

        var content = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var crc = Crc32.Compute(content);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(content);
        Span<byte> crcBytes = stackalloc byte[CrcTrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(crcBytes, crc);
        stream.Write(crcBytes);
        stream.Flush(sync);
    }

    public static void LoadInto(string path, MemTable mem)
    {
        if (!TryLoadInto(path, mem))
            throw new InvalidDataException($"leveldb: bad or unsupported table in {path}");
    }

    /// <summary>
    /// Loads a ZLDB snapshot. Returns false only when the file isn't Zenith format at all (e.g.
    /// leftover native LevelDB MANIFEST — no magic match), so callers can recover empty in that
    /// case. A file that *is* ZLDB format but fails its trailing checksum is a different failure
    /// mode — genuine corruption of our own data, not a foreign file — and throws instead of
    /// silently discarding it, so a corrupted product snapshot fails loudly rather than quietly
    /// resetting a world to empty.
    /// </summary>
    public static bool TryLoadInto(string path, MemTable mem)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < CrcTrailerSize)
            return false;

        var contentLength = bytes.Length - CrcTrailerSize;
        using var stream = new MemoryStream(bytes, 0, contentLength, writable: false);
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadBytes(Math.Min(4, contentLength));
        if (magic.Length != 4 || !magic.AsSpan().SequenceEqual(Magic))
            return false;

        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(contentLength, CrcTrailerSize));
        var actualCrc = Crc32.Compute(bytes.AsSpan(0, contentLength));
        if (actualCrc != storedCrc)
            throw new InvalidDataException(
                $"leveldb: snapshot {path} failed checksum verification (corrupted) — refusing to load it as live data");

        var version = reader.ReadUInt32();
        if (version != Version)
            return false;

        var count = reader.ReadInt32();
        if (count < 0)
            return false;

        for (var i = 0; i < count; i++)
        {
            var keyLen = reader.ReadInt32();
            if (keyLen < 0) return false;
            var key = reader.ReadBytes(keyLen);
            var valLen = reader.ReadUInt32();
            if (valLen == uint.MaxValue)
            {
                mem.Delete(key);
                continue;
            }

            if (valLen > int.MaxValue) return false;
            var value = reader.ReadBytes((int)valLen);
            mem.Put(key, value);
        }

        return true;
    }
}
