namespace Zenith.LevelDB;

/// <summary>
/// WAL append-only.
/// Record types: 1=put, 2=delete (legacy single-op), 3=batch (payload = WriteBatch.Encode).
/// Legacy: u8 type | i32 keyLen | key | i32 valLen | value.
/// Batch: u8 type=3 | i32 payloadLen | payload.
/// </summary>
sealed class JournalWriter : IDisposable
{
    public const byte TypePut = 1;
    public const byte TypeDelete = 2;
    public const byte TypeBatch = 3;

    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;

    public JournalWriter(string path, bool append)
    {
        _stream = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_stream);
    }

    public void AppendPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        _writer.Write(TypePut);
        _writer.Write(key.Length);
        _writer.Write(key);
        _writer.Write(value.Length);
        _writer.Write(value);
    }

    public void AppendDelete(ReadOnlySpan<byte> key)
    {
        _writer.Write(TypeDelete);
        _writer.Write(key.Length);
        _writer.Write(key);
        _writer.Write(0);
    }

    public void AppendBatch(ReadOnlySpan<byte> encodedBatch)
    {
        _writer.Write(TypeBatch);
        _writer.Write(encodedBatch.Length);
        _writer.Write(encodedBatch);
    }

    public void Flush(bool sync)
    {
        _writer.Flush();
        _stream.Flush(sync);
    }

    public void Dispose()
    {
        _writer.Dispose();
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
                if (stream.Position + 4 > stream.Length) break;
                var payloadLen = reader.ReadInt32();
                if (payloadLen < 0 || stream.Position + payloadLen > stream.Length) break;
                var payload = reader.ReadBytes(payloadLen);
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

            if (stream.Position + 4 > stream.Length) break;
            var keyLen = reader.ReadInt32();
            if (keyLen < 0 || stream.Position + keyLen + 4 > stream.Length) break;
            var key = reader.ReadBytes(keyLen);
            var valLen = reader.ReadInt32();
            if (valLen < 0 || stream.Position + valLen > stream.Length) break;

            if (type == TypePut)
            {
                var value = reader.ReadBytes(valLen);
                mem.Put(key, value);
            }
            else if (type == TypeDelete)
            {
                if (valLen > 0) reader.ReadBytes(valLen);
                mem.Delete(key);
            }
            else
            {
                break;
            }
        }
    }
}
