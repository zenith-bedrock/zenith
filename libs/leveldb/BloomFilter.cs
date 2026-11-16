namespace Zenith.LevelDB;

/// <summary>
/// Per-table bloom filter. A read for a key the table does not hold (the common case when a world asks for a
/// column that was never saved) is answered from memory instead of reading a block.
/// Serialized as <c>probes:u8 | bit array</c>.
/// </summary>
sealed class BloomFilter
{
    private readonly byte[] _data;
    private readonly int _probes;
    private readonly ulong _bitCount;

    public BloomFilter(byte[] serialized)
    {
        if (serialized.Length < 2) throw new InvalidDataException("leveldb: bloom filter is empty");
        _data = serialized;
        _probes = serialized[0];
        _bitCount = (ulong)(serialized.Length - 1) * 8;
    }

    public int SizeInBytes => _data.Length;

    public static ulong Hash(ReadOnlySpan<byte> key)
    {
        // FNV-1a, finished with a multiply-xorshift so that the high and low halves both carry entropy.
        var h = 14695981039346656037UL;
        foreach (var b in key)
        {
            h ^= b;
            h *= 1099511628211UL;
        }

        h ^= h >> 32;
        h *= 0x9E3779B97F4A7C15UL;
        h ^= h >> 29;
        return h;
    }

    public static byte[] Build(IReadOnlyList<ulong> hashes, int bitsPerKey)
    {
        var bits = Math.Max(64, (long)hashes.Count * bitsPerKey);
        var bytes = (int)((bits + 7) / 8);
        var probes = Math.Clamp((int)Math.Round(bitsPerKey * 0.69), 1, 16);
        var data = new byte[1 + bytes];
        data[0] = (byte)probes;
        var bitCount = (ulong)bytes * 8;
        foreach (var hash in hashes)
            for (var i = 0; i < probes; i++)
            {
                var bit = Probe(hash, i) % bitCount;
                data[1 + (int)(bit >> 3)] |= (byte)(1 << (int)(bit & 7));
            }

        return data;
    }

    public bool MayContain(ulong hash)
    {
        for (var i = 0; i < _probes; i++)
        {
            var bit = Probe(hash, i) % _bitCount;
            if ((_data[1 + (int)(bit >> 3)] & (1 << (int)(bit & 7))) == 0) return false;
        }

        return true;
    }

    private static ulong Probe(ulong hash, int i) => (uint)hash + (ulong)i * ((hash >> 32) | 1);
}
