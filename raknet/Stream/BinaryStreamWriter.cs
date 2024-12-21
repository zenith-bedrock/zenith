using System.Buffers.Binary;
using System.Text;

namespace Zenith.Raknet.Stream;

public class BinaryStreamWriter : IDisposable
{
    private readonly MemoryStream _stream = new();

    public void WriteByte(byte value)
    {
        _stream.WriteByte(value);
    }
    
    public void WriteBool(bool value)
    {
        _stream.WriteByte(value ? (byte)1 : (byte)0);
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt16BE((ushort)bytes.Length);
        _stream.Write(bytes);
    }

    public void WriteMagic()
    {
        _stream.Write(RakNetServer.MAGIC);
    }

    public void WriteUInt16LE(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUInt16BE(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUInt64LE(ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUInt64BE(ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        _stream.Write(buffer);
    }

    public byte[] GetBuffer()
    {
        return _stream.GetBuffer();
    }
    
    public byte[] GetBufferDisposing()
    {
        var buffer = GetBuffer();
        Dispose();
        return buffer;
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}