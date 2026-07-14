namespace Zenith.LevelDB;

/// <summary>
/// Snapshot on-disk ordenado (formato Zenith ZLDB — não compatível com LevelDB.Standard/NuGet).
/// Header: magic "ZLDB" | version u32=1 | count u32 | entries: u32 klen | key | u32 vlen | value.
/// Tombstones não são gravados no snapshot compactado (só live keys).
/// </summary>
static class TableFile
{
    private static readonly byte[] Magic = "ZLDB"u8.ToArray();
    private const uint Version = 1;

    public static void Write(string path, IEnumerable<KeyValuePair<byte[], byte[]>> liveEntries, bool sync)
    {
        var list = new List<KeyValuePair<byte[], byte[]>>(liveEntries);
        list.Sort((a, b) => ByteComparer.Instance.Compare(a.Key, b.Key));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
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

        writer.Flush();
        stream.Flush(sync);
    }

    public static void LoadInto(string path, MemTable mem)
    {
        if (!TryLoadInto(path, mem))
            throw new InvalidDataException($"leveldb: bad or unsupported table in {path}");
    }

    /// <summary>
    /// Loads a ZLDB snapshot. Returns false if the file is not Zenith format
    /// (e.g. leftover native LevelDB MANIFEST) so callers can recover empty.
    /// </summary>
    public static bool TryLoadInto(string path, MemTable mem)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || !magic.AsSpan().SequenceEqual(Magic))
            return false;

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
