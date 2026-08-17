namespace Zenith.LevelDB;

/// <summary>
/// Table-driven CRC-32 (IEEE 802.3 polynomial 0xEDB88320 reflected) — same family of algorithm
/// real LevelDB uses (CRC32C/Castagnoli) for WAL record and SSTable block checksums, but plain
/// IEEE here rather than Castagnoli: ZLDB's on-disk format is already explicitly declared
/// non-compatible with Mojang/RocksDB/NuGet LevelDB (see <c>TableFile</c>'s doc comment), so there
/// is no reader on the other end that needs the exact same polynomial — only the checksumming
/// *strategy* (checksum type+payload, not length) is worth mirroring, not the specific algorithm.
/// </summary>
static class Crc32
{
    private const uint Polynomial = 0xEDB88320;
    private const uint InitialValue = 0xFFFFFFFF;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = InitialValue;
        foreach (var b in data)
            crc = Table[(byte)(crc ^ b)] ^ (crc >> 8);
        return crc ^ InitialValue;
    }

    /// <summary>Computes the checksum over a leading byte (typically a record type tag) followed
    /// by <paramref name="data"/>, without allocating a combined buffer.</summary>
    public static uint Compute(byte prefix, ReadOnlySpan<byte> data)
    {
        var crc = InitialValue;
        crc = Table[(byte)(crc ^ prefix)] ^ (crc >> 8);
        foreach (var b in data)
            crc = Table[(byte)(crc ^ b)] ^ (crc >> 8);
        return crc ^ InitialValue;
    }
}
