namespace Zenith.LevelDB;

/// <summary>
/// WAL append-only.
/// Record types: 1=put, 2=delete (legacy single-op), 3=batch (payload = WriteBatch.Encode).
/// Legacy: u8 type | i32 keyLen | key | i32 valLen | value.
/// Batch: u8 type=3 | i32 payloadLen | payload.
/// Both length-prefixed pieces go through <see cref="KvFraming"/> (shared with WriteBatch's own
/// op encoding, which uses the identical int32-LE-length shape).
/// </summary>
sealed class JournalWriter : IDisposable
{
    public const byte TypePut = 1;
    public const byte TypeDelete = 2;
    public const byte TypeBatch = 3;

    private readonly FileStream _stream;

    public JournalWriter(string path, bool append)
    {
        _stream = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    public void AppendPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        _stream.WriteByte(TypePut);
        KvFraming.WriteLengthPrefixed(_stream, key);
        KvFraming.WriteLengthPrefixed(_stream, value);
    }

    public void AppendDelete(ReadOnlySpan<byte> key)
    {
        _stream.WriteByte(TypeDelete);
        KvFraming.WriteLengthPrefixed(_stream, key);
        KvFraming.WriteLengthPrefixed(_stream, []);
    }

    public void AppendBatch(ReadOnlySpan<byte> encodedBatch)
    {
        _stream.WriteByte(TypeBatch);
        KvFraming.WriteLengthPrefixed(_stream, encodedBatch);
    }

    public void Flush(bool sync)
    {
        _stream.Flush(sync);
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    public static void Replay(string path, MemTable mem)
    {
        if (!File.Exists(path)) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        while (stream.Position < stream.Length)
        {
            byte type;
            try
            {
                type = reader.ReadByte();
            }
            catch (EndOfStreamException)
            {
                break;
            }

            if (type == TypeBatch)
            {
                if (!KvFraming.TryReadLengthPrefixed(reader, stream.Length, out var payload)) break;
                try
                {
                    WriteBatch.ApplyEncoded(payload, mem);
                }
                catch (InvalidDataException)
                {
                    break;
                }

                continue;
            }

            if (!KvFraming.TryReadLengthPrefixed(reader, stream.Length, out var key)) break;
            if (!KvFraming.TryReadLengthPrefixed(reader, stream.Length, out var value)) break;

            if (type == TypePut)
            {
                mem.Put(key, value);
            }
            else if (type == TypeDelete)
            {
                mem.Delete(key);
            }
            else
            {
                break;
            }
        }
    }
}
