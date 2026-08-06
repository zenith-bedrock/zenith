using System.Buffers.Binary;

namespace Zenith.LevelDB;

/// <summary>
/// Shared length-prefixed byte-blob framing used by <see cref="JournalWriter"/> (WAL Put/Delete
/// records) and <see cref="WriteBatch"/> (batch ops) - both encode "int32 LE length | bytes" and
/// used to do so via two independently hand-rolled implementations (BinaryWriter vs raw
/// BinaryPrimitives) that could silently drift in length-field width/endianness. Not used by
/// <see cref="TableFile"/>: its value length field is deliberately uint32 with a
/// <c>uint.MaxValue</c> tombstone sentinel (a real format difference, not an oversight - see the
/// comment there), so it only shares the key-length half of its framing with this helper.
/// </summary>
static class KvFraming
{
    public const int LengthFieldSize = sizeof(int);

    public static void WriteLengthPrefixed(Stream stream, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBuf = stackalloc byte[LengthFieldSize];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, data.Length);
        stream.Write(lenBuf);
        stream.Write(data);
    }

    public static void WriteLengthPrefixed(Span<byte> destination, ref int pos, ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(pos, LengthFieldSize), data.Length);
        pos += LengthFieldSize;
        data.CopyTo(destination.Slice(pos));
        pos += data.Length;
    }

    /// <summary>Bounds-checked read against a stream of known length - returns false (instead of
    /// throwing) on truncation or a negative length, so callers can treat it as "stop replaying"
    /// rather than a hard failure.</summary>
    public static bool TryReadLengthPrefixed(BinaryReader reader, long streamLength, out byte[] data)
    {
        data = [];
        var stream = reader.BaseStream;
        if (stream.Position + LengthFieldSize > streamLength) return false;

        var length = reader.ReadInt32();
        if (length < 0 || stream.Position + length > streamLength) return false;

        data = reader.ReadBytes(length);
        return true;
    }

    /// <summary>Bounds-checked read over an in-memory span (WriteBatch's decode path operates on
    /// an already-buffered payload, not a Stream, so this avoids wrapping it just to reuse the
    /// Stream-based overload).</summary>
    public static bool TryReadLengthPrefixed(ReadOnlySpan<byte> source, ref int pos, out ReadOnlySpan<byte> data)
    {
        data = default;
        if (pos + LengthFieldSize > source.Length) return false;

        var length = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(pos, LengthFieldSize));
        pos += LengthFieldSize;
        if (length < 0 || pos + length > source.Length) return false;

        data = source.Slice(pos, length);
        pos += length;
        return true;
    }
}
