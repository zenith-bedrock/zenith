namespace Zenith.LevelDB;

/// <summary>
/// Tabela imutável ordenada em disco (formato Zenith — não compatível com LevelDB.Standard/NuGet).
/// Header: magic "ZLDB" | version u32=1 | count u32 | entries: u32 klen | key | u32 vlen | value.
/// Tombstone: vlen = <see cref="Tombstone"/>.
/// </summary>
static class TableFile
{
    private static readonly byte[] Magic = "ZLDB"u8.ToArray();
    private const uint Version = 1;
    public const uint Tombstone = uint.MaxValue;

    public static void Write(string path, IEnumerable<KeyValuePair<byte[], byte[]?>> entries)
    {
        var list = new List<KeyValuePair<byte[], byte[]?>>(entries);
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
            if (value is null)
            {
                writer.Write(Tombstone);
            }
            else
            {
                writer.Write((uint)value.Length);
                writer.Write(value);
            }
        }
    }

    public static bool TryGet(string path, byte[] key, out byte[]? value, out bool deleted)
    {
        value = null;
        deleted = false;
        foreach (var (k, v) in ScanFrom(path, key))
        {
            var cmp = ByteComparer.Compare(k, key);
            if (cmp == 0)
            {
                deleted = v is null;
                value = v;
                return true;
            }

            if (cmp > 0) break;
        }

        return false;
    }

    /// <summary>
    /// Yields entries with key &gt;= seekKey in sorted order, streaming from disk.
    /// </summary>
    public static IEnumerable<(byte[] Key, byte[]? Value)> ScanFrom(string path, byte[] seekKey)
    {
        ArgumentNullException.ThrowIfNull(seekKey);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || !magic.AsSpan().SequenceEqual(Magic))
            yield break;

        var version = reader.ReadUInt32();
        if (version != Version)
            throw new InvalidDataException($"leveldb: unsupported table version {version} in {path}");

        var count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException($"leveldb: bad table count in {path}");

        for (var i = 0; i < count; i++)
        {
            var keyLen = reader.ReadInt32();
            if (keyLen < 0) throw new InvalidDataException($"leveldb: bad key length in {path}");
            var key = reader.ReadBytes(keyLen);
            var valLen = reader.ReadUInt32();
            byte[]? value;
            if (valLen == Tombstone)
            {
                value = null;
            }
            else
            {
                if (valLen > int.MaxValue) throw new InvalidDataException($"leveldb: value too large in {path}");
                value = reader.ReadBytes((int)valLen);
            }

            if (ByteComparer.Compare(key, seekKey) < 0)
                continue;

            yield return (key, value);
        }
    }

    public static IEnumerable<KeyValuePair<byte[], byte[]?>> ReadAll(string path)
    {
        foreach (var (k, v) in ScanFrom(path, []))
            yield return new KeyValuePair<byte[], byte[]?>(k, v);
    }
}
