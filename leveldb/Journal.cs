namespace Zenith.LevelDB;

/// <summary>
/// WAL append-only. Record: u8 type | u32 keyLen | key | u32 valLen | value (valLen=0 se delete).
/// Type: 1=put, 2=delete.
/// </summary>
sealed class JournalWriter : IDisposable
{
    public const byte TypePut = 1;
    public const byte TypeDelete = 2;

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
