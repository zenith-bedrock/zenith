using System.Buffers.Binary;

namespace Zenith.LevelDB;

/// <summary>
/// WAL append-only.
/// Record types: 1=put, 2=delete (legacy single-op), 3=batch (payload = WriteBatch.Encode).
/// Envelope (every record): type:u8 | crc:u32 LE | bodyLen:u32 LE | body[bodyLen].
/// <c>crc</c> = <see cref="Crc32.Compute(byte, ReadOnlySpan{byte})"/> over type+body (not bodyLen —
/// mirrors real LevelDB's WAL record checksum, which also excludes its own length field). Body
/// contents: legacy Put/Delete = key (length-prefixed) + value (length-prefixed, empty for
/// Delete); Batch = payload (length-prefixed <see cref="WriteBatch"/> encoding). Both
/// length-prefixed pieces inside the body go through <see cref="KvFraming"/> (shared with
/// WriteBatch's own op encoding, which uses the identical int32-LE-length shape).
/// </summary>
sealed class JournalWriter : IDisposable
{
    public const byte TypePut = 1;
    public const byte TypeDelete = 2;
    public const byte TypeBatch = 3;

    private const int HeaderSize = 1 + 4 + 4; // type + crc + bodyLen

    private readonly FileStream _stream;

    public JournalWriter(string path, bool append)
    {
        _stream = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    public void AppendPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        using var body = new MemoryStream(key.Length + value.Length + 2 * KvFraming.LengthFieldSize);
        KvFraming.WriteLengthPrefixed(body, key);
        KvFraming.WriteLengthPrefixed(body, value);
        WriteRecord(TypePut, body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    public void AppendDelete(ReadOnlySpan<byte> key)
    {
        using var body = new MemoryStream(key.Length + 2 * KvFraming.LengthFieldSize);
        KvFraming.WriteLengthPrefixed(body, key);
        KvFraming.WriteLengthPrefixed(body, []);
        WriteRecord(TypeDelete, body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    public void AppendBatch(ReadOnlySpan<byte> encodedBatch)
    {
        using var body = new MemoryStream(encodedBatch.Length + KvFraming.LengthFieldSize);
        KvFraming.WriteLengthPrefixed(body, encodedBatch);
        WriteRecord(TypeBatch, body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    private void WriteRecord(byte type, ReadOnlySpan<byte> body)
    {
        var crc = Crc32.Compute(type, body);
        Span<byte> header = stackalloc byte[HeaderSize];
        header[0] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(1, 4), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(5, 4), (uint)body.Length);
        _stream.Write(header);
        _stream.Write(body);
    }

    public void Flush(bool sync)
    {
        _stream.Flush(sync);
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    /// <summary>
    /// Replays every intact, checksum-valid record into <paramref name="mem"/>.
    /// Two distinct stop conditions, matching real LevelDB's WAL-replay behavior:
    /// a header/body that runs past end-of-file is a normal crash-torn tail (silent stop, not an
    /// error — the writer died mid-append, everything before it is still durable); a
    /// structurally-complete record whose CRC doesn't match its bytes is genuine corruption —
    /// replay stops there and reports via <paramref name="onCorruption"/> rather than trusting
    /// anything after it, since a corrupted record can't be relied on to point at a valid next
    /// record either.
    /// </summary>
    public static void Replay(string path, MemTable mem, Action<string>? onCorruption = null)
    {
        if (!File.Exists(path)) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        while (stream.Position < stream.Length)
        {
            var recordStart = stream.Position;

            if (stream.Length - stream.Position < HeaderSize) break; // torn header at tail = EOF

            var type = reader.ReadByte();
            var storedCrc = reader.ReadUInt32();
            var bodyLen = reader.ReadUInt32();

            if (bodyLen > int.MaxValue || stream.Position + bodyLen > stream.Length)
                break; // torn body at tail = EOF, expected after a crash mid-append

            var body = reader.ReadBytes((int)bodyLen);
            var actualCrc = Crc32.Compute(type, body);
            if (actualCrc != storedCrc)
            {
                onCorruption?.Invoke(
                    $"leveldb: WAL corruption at offset {recordStart} (CRC mismatch on a structurally " +
                    "complete record) — stopping replay, discarding this record and everything after it");
                break;
            }

            if (type == TypeBatch)
            {
                var pos = 0;
                if (!KvFraming.TryReadLengthPrefixed(body, ref pos, out var payload))
                {
                    onCorruption?.Invoke(
                        $"leveldb: WAL batch framing invalid at offset {recordStart} despite a valid CRC — stopping replay");
                    break;
                }

                try
                {
                    WriteBatch.ApplyEncoded(payload, mem);
                }
                catch (InvalidDataException ex)
                {
                    onCorruption?.Invoke(
                        $"leveldb: WAL batch content invalid at offset {recordStart} despite a valid CRC: {ex.Message} — stopping replay");
                    break;
                }

                continue;
            }

            {
                var pos = 0;
                if (!KvFraming.TryReadLengthPrefixed(body, ref pos, out var key) ||
                    !KvFraming.TryReadLengthPrefixed(body, ref pos, out var value))
                {
                    onCorruption?.Invoke(
                        $"leveldb: WAL record framing invalid at offset {recordStart} despite a valid CRC — stopping replay");
                    break;
                }

                if (type == TypePut)
                {
                    mem.Put(key.ToArray(), value.ToArray());
                }
                else if (type == TypeDelete)
                {
                    mem.Delete(key.ToArray());
                }
                else
                {
                    onCorruption?.Invoke(
                        $"leveldb: unknown WAL record type {type} at offset {recordStart} despite a valid CRC — stopping replay");
                    break;
                }
            }
        }
    }
}
