using System.Buffers.Binary;
using System.Net;
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

    public void WriteAddress(IPEndPoint value)
    {
        WriteByte(4); // TODO: support ipv6

        var octets = value.Address.GetAddressBytes();
        WriteByte((byte)(~octets[0] & 0xff));
        WriteByte((byte)(~octets[1] & 0xff));
        WriteByte((byte)(~octets[2] & 0xff));
        WriteByte((byte)(~octets[3] & 0xff));

        WriteUInt16BE((ushort)value.Port);
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

    public void WriteUInt32LE(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUInt32BE(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
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