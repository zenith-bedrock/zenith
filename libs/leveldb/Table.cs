using System.Buffers;
using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace Zenith.LevelDB;

/// <summary>Result of a point lookup in one table: a key can be absent, present, or deleted here (a tombstone).</summary>
enum TableLookup
{
    NotFound,
    Found,
    Deleted,
}

/// <summary>
/// One immutable, sorted, on-disk table (ZLDB v2, ADR §168). The file is read-only after it is written, so any
/// number of threads read it at once without a lock. Only the block index and the bloom filter stay in RAM; a
/// lookup reads at most one data block.
/// <code>
/// [block]* [bloom] [index] [footer]
/// block  = entries | crc32            entry = flags u8 | keyLen varint | (valueLen varint, only for a put) | key | value
/// bloom  = BloomFilter | crc32        flags = 0 put, 1 tombstone
/// index  = (keyLen varint | firstKey | offset u64 | length u32)* | crc32
/// footer = magic u32 | version u32 | entries u64 | bloomOffset u64 | bloomLength u32 | indexOffset u64 | indexLength u32 | crc32
/// </code>
/// Integers are little-endian. A block's length excludes its trailing crc.
/// </summary>
sealed class Table : IDisposable
{
    internal const uint Magic = 0x32444C5A; // "ZLD2" as bytes
    internal const uint FormatVersion = 2;
    internal const int FooterSize = 44;
    internal const int CrcSize = sizeof(uint);
    internal const byte FlagPut = 0;
    internal const byte FlagTombstone = 1;

    private readonly SafeFileHandle _handle;
    private readonly byte[][] _firstKeys;
    private readonly long[] _offsets;
    private readonly int[] _lengths;
    private readonly BloomFilter _bloom;

    private Table(string path, SafeFileHandle handle, long fileLength, long entryCount,
        byte[][] firstKeys, long[] offsets, int[] lengths, BloomFilter bloom)
    {
        Path = path;
        _handle = handle;
        FileLength = fileLength;
        EntryCount = entryCount;
        _firstKeys = firstKeys;
        _offsets = offsets;
        _lengths = lengths;
        _bloom = bloom;
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public long FileLength { get; }

    /// <summary>Entries in this file, tombstones included.</summary>
    public long EntryCount { get; }

    public int BlockCount => _firstKeys.Length;

    /// <summary>Bytes this table keeps in RAM: its index keys and its bloom filter.</summary>
    public long ResidentBytes
    {
        get
        {
            long total = _bloom.SizeInBytes + _offsets.Length * (sizeof(long) + sizeof(int));
            foreach (var key in _firstKeys) total += key.Length;
            return total;
        }
    }

    /// <summary>
    /// Opens a table. False when the file is not a ZLDB v2 table at all (too short, other magic or version), so the
    /// caller can treat it as foreign residue. A file that is one but fails a checksum throws
    /// <see cref="InvalidDataException"/>: that is damage, not a stranger.
    /// </summary>
    public static bool TryOpen(string path, out Table? table)
    {
        table = null;
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, FileOptions.RandomAccess);
        try
        {
            var length = RandomAccess.GetLength(handle);
            if (length < FooterSize) { handle.Dispose(); return false; }

            Span<byte> footer = stackalloc byte[FooterSize];
            RandomAccess.Read(handle, footer, length - FooterSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(footer) != Magic ||
                BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]) != FormatVersion)
            {
                handle.Dispose();
                return false;
            }

            if (Crc32.Compute(footer[..(FooterSize - CrcSize)]) != BinaryPrimitives.ReadUInt32LittleEndian(footer[(FooterSize - CrcSize)..]))
                throw new InvalidDataException($"leveldb: table {path} failed footer checksum (corrupted)");

            var entries = BinaryPrimitives.ReadInt64LittleEndian(footer[8..]);
            var bloomOffset = BinaryPrimitives.ReadInt64LittleEndian(footer[16..]);
            var bloomLength = BinaryPrimitives.ReadInt32LittleEndian(footer[24..]);
            var indexOffset = BinaryPrimitives.ReadInt64LittleEndian(footer[28..]);
            var indexLength = BinaryPrimitives.ReadInt32LittleEndian(footer[36..]);

            var bloom = new BloomFilter(ReadSection(handle, path, "bloom", bloomOffset, bloomLength, length));
            var index = ReadSection(handle, path, "index", indexOffset, indexLength, length);
            ParseIndex(path, index, out var firstKeys, out var offsets, out var lengths);

            table = new Table(path, handle, length, entries, firstKeys, offsets, lengths, bloom);
            return true;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static Table Open(string path) =>
        TryOpen(path, out var table) ? table! : throw new InvalidDataException($"leveldb: {path} is not a ZLDB table");

    /// <summary>Looks <paramref name="key"/> up. At most one block is read, and none when the bloom filter says no.</summary>
    public TableLookup TryGet(byte[] key, out byte[]? value)
    {
        value = null;
        if (_firstKeys.Length == 0 || !_bloom.MayContain(BloomFilter.Hash(key))) return TableLookup.NotFound;
        var block = FindBlock(key);
        if (block < 0) return TableLookup.NotFound;

        var rented = ArrayPool<byte>.Shared.Rent(_lengths[block] + CrcSize);
        try
        {
            var payload = ReadBlock(block, rented);
            var position = 0;
            while (TryParseEntry(payload, ref position, out var entryKey, out var entryValue, out var tombstone))
            {
                var order = ByteComparer.Compare(entryKey, key);
                if (order == 0)
                {
                    if (tombstone) return TableLookup.Deleted;
                    value = entryValue.ToArray();
                    return TableLookup.Found;
                }

                if (order > 0) break; // entries are sorted: we are past it
            }

            return TableLookup.NotFound;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Index of the last block whose first key is &lt;= <paramref name="key"/>, or -1 when the key sorts before all.</summary>
    internal int FindBlock(ReadOnlySpan<byte> key)
    {
        int lo = 0, hi = _firstKeys.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (ByteComparer.Compare(_firstKeys[mid], key) <= 0) lo = mid + 1;
            else hi = mid;
        }

        return lo - 1;
    }

    /// <summary>Reads and verifies block <paramref name="index"/> into <paramref name="buffer"/>; returns its payload.</summary>
    internal ReadOnlySpan<byte> ReadBlock(int index, byte[] buffer)
    {
        var length = _lengths[index];
        var read = RandomAccess.Read(_handle, buffer.AsSpan(0, length + CrcSize), _offsets[index]);
        if (read != length + CrcSize)
            throw new InvalidDataException($"leveldb: table {Path} block {index} is truncated");
        var stored = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(length, CrcSize));
        if (Crc32.Compute(buffer.AsSpan(0, length)) != stored)
            throw new InvalidDataException($"leveldb: table {Path} block {index} failed its checksum (corrupted)");
        return buffer.AsSpan(0, length);
    }

    internal int BlockLength(int index) => _lengths[index];

    /// <summary>Reads every block and checks every checksum. Used by repair and by tests; reads never need it.</summary>
    public void Verify()
    {
        var buffer = new byte[_lengths.Length == 0 ? 0 : _lengths.Max() + CrcSize];
        for (var i = 0; i < _lengths.Length; i++)
            ReadBlock(i, buffer);
    }

    /// <summary>Parses the entry at <paramref name="position"/>; false at the end of the block.</summary>
    internal static bool TryParseEntry(ReadOnlySpan<byte> block, ref int position,
        out ReadOnlySpan<byte> key, out ReadOnlySpan<byte> value, out bool tombstone)
    {
        key = default;
        value = default;
        tombstone = false;
        if (position >= block.Length) return false;

        var flags = block[position++];
        tombstone = flags == FlagTombstone;
        if (!Varint.TryRead(block, ref position, out var keyLength)) return Corrupt();
        uint valueLength = 0;
        if (!tombstone && !Varint.TryRead(block, ref position, out valueLength)) return Corrupt();
        if ((long)position + keyLength + valueLength > block.Length) return Corrupt();

        key = block.Slice(position, (int)keyLength);
        position += (int)keyLength;
        value = block.Slice(position, (int)valueLength);
        position += (int)valueLength;
        return true;
    }

    private static bool Corrupt() => throw new InvalidDataException("leveldb: malformed table entry");

    private static byte[] ReadSection(SafeFileHandle handle, string path, string name, long offset, int length, long fileLength)
    {
        if (offset < 0 || length < 0 || offset + length + CrcSize > fileLength - FooterSize)
            throw new InvalidDataException($"leveldb: table {path} has an invalid {name} section");
        var buffer = new byte[length + CrcSize];
        if (RandomAccess.Read(handle, buffer, offset) != buffer.Length)
            throw new InvalidDataException($"leveldb: table {path} {name} section is truncated");
        var stored = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(length));
        if (Crc32.Compute(buffer.AsSpan(0, length)) != stored)
            throw new InvalidDataException($"leveldb: table {path} {name} section failed its checksum (corrupted)");
        return buffer.AsSpan(0, length).ToArray();
    }

    private static void ParseIndex(string path, byte[] index, out byte[][] firstKeys, out long[] offsets, out int[] lengths)
    {
        var keys = new List<byte[]>();
        var offs = new List<long>();
        var lens = new List<int>();
        var position = 0;
        while (position < index.Length)
        {
            if (!Varint.TryRead(index, ref position, out var keyLength) ||
                (long)position + keyLength + sizeof(long) + sizeof(int) > index.Length)
                throw new InvalidDataException($"leveldb: table {path} has a malformed index");
            keys.Add(index.AsSpan(position, (int)keyLength).ToArray());
            position += (int)keyLength;
            offs.Add(BinaryPrimitives.ReadInt64LittleEndian(index.AsSpan(position)));
            position += sizeof(long);
            lens.Add(BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(position)));
            position += sizeof(int);
        }

        firstKeys = [.. keys];
        offsets = [.. offs];
        lengths = [.. lens];
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>Writes a <see cref="Table"/> from already-sorted entries, streaming: memory use is one block.</summary>
static class TableWriter
{
    /// <summary>
    /// Writes <paramref name="sortedEntries"/> (ascending, a null value is a tombstone) to <paramref name="path"/>.
    /// The file is built under a <c>.tmp</c> name and renamed once complete, so a crash never leaves a half table
    /// under a real name. Returns how many entries it kept.
    /// </summary>
    public static long Write(string path, IEnumerable<KeyValuePair<byte[], byte[]?>> sortedEntries,
        int blockSize, int bloomBitsPerKey, bool dropTombstones, bool sync, CancellationToken cancellationToken = default)
    {
        var temporary = path + ".tmp";
        var index = new MemoryStream();
        var hashes = new List<ulong>();
        var block = new MemoryStream();
        byte[]? blockFirstKey = null;
        long entries = 0;

        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            void FlushBlock()
            {
                if (blockFirstKey is null) return;
                var payload = block.GetBuffer().AsSpan(0, (int)block.Length);
                AppendIndexEntry(index, blockFirstKey, file.Position, payload.Length);
                file.Write(payload);
                WriteCrc(file, Crc32.Compute(payload));
                block.SetLength(0);
                blockFirstKey = null;
            }

            Span<byte> lengthBuffer = stackalloc byte[Varint.MaxLength];
            foreach (var (key, value) in sortedEntries)
            {
                if (value is null && dropTombstones) continue;
                cancellationToken.ThrowIfCancellationRequested();

                blockFirstKey ??= key;
                block.WriteByte(value is null ? Table.FlagTombstone : Table.FlagPut);
                block.Write(lengthBuffer[..Varint.Write(lengthBuffer, (uint)key.Length)]);
                if (value is not null)
                    block.Write(lengthBuffer[..Varint.Write(lengthBuffer, (uint)value.Length)]);
                block.Write(key);
                if (value is not null) block.Write(value);

                hashes.Add(BloomFilter.Hash(key));
                entries++;
                if (block.Length >= blockSize) FlushBlock();
            }

            FlushBlock();

            var bloomOffset = file.Position;
            var bloom = BloomFilter.Build(hashes, bloomBitsPerKey);
            file.Write(bloom);
            WriteCrc(file, Crc32.Compute(bloom));

            var indexOffset = file.Position;
            var indexBytes = index.GetBuffer().AsSpan(0, (int)index.Length);
            file.Write(indexBytes);
            WriteCrc(file, Crc32.Compute(indexBytes));

            Span<byte> footer = stackalloc byte[Table.FooterSize];
            BinaryPrimitives.WriteUInt32LittleEndian(footer, Table.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], Table.FormatVersion);
            BinaryPrimitives.WriteInt64LittleEndian(footer[8..], entries);
            BinaryPrimitives.WriteInt64LittleEndian(footer[16..], bloomOffset);
            BinaryPrimitives.WriteInt32LittleEndian(footer[24..], bloom.Length);
            BinaryPrimitives.WriteInt64LittleEndian(footer[28..], indexOffset);
            BinaryPrimitives.WriteInt32LittleEndian(footer[36..], indexBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(footer[40..], Crc32.Compute(footer[..40]));
            file.Write(footer);
            file.Flush(sync);
        }

        File.Move(temporary, path, overwrite: true);
        return entries;
    }

    private static void AppendIndexEntry(MemoryStream index, byte[] firstKey, long offset, int length)
    {
        Span<byte> buffer = stackalloc byte[Varint.MaxLength];
        index.Write(buffer[..Varint.Write(buffer, (uint)firstKey.Length)]);
        index.Write(firstKey);
        Span<byte> fixedPart = stackalloc byte[sizeof(long) + sizeof(int)];
        BinaryPrimitives.WriteInt64LittleEndian(fixedPart, offset);
        BinaryPrimitives.WriteInt32LittleEndian(fixedPart[sizeof(long)..], length);
        index.Write(fixedPart);
    }

    private static void WriteCrc(Stream stream, uint crc)
    {
        Span<byte> bytes = stackalloc byte[Table.CrcSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, crc);
        stream.Write(bytes);
    }
}
