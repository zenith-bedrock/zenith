using System.Buffers.Binary;
using System.Text;

namespace Zenith.Raknet.Stream;

public class BinaryStream : IDisposable
{
    public enum Endianess : byte
    {
        Big,
        Little
    }
    
    public int Offset { get; set; }
    public byte[] Buffer { get; private set; }

    public int Length => Buffer.Length;

    public bool IsEndOfFile => Offset >= Buffer.Length;
    
    public Span<byte> GetBufferDisposing()
    {
        var buffer = Buffer;
        Dispose();
        return buffer.AsSpan();
    }

    public BinaryStream(byte[]? buffer = null, int offset = 0)
    {
        Buffer = buffer ?? Array.Empty<byte>();
        Offset = offset;
    }

    public void Rewind() => Offset = 0;

    public Span<byte> ReadSpan(int len)
    {
        switch (len)
        {
            case < 0:
                throw new ArgumentException("Length must be positive");
            case 0:
                return Span<byte>.Empty;
        }
        var remaining = Buffer.Length - Offset;
        if (remaining < len)
        {
            throw new InvalidOperationException($"Not enough bytes left in buffer: need {len}, have {remaining}");
        }

        var result = new Span<byte>(Buffer, Offset, len);
        Offset += len;
        return result;
    }

    public Span<byte> ReadRemaining()
    {
        if (Offset >= Buffer.Length)
        {
            throw new InvalidOperationException("No bytes left to read");
        }

        var remainingSpan = new Span<byte>(Buffer, Offset, Buffer.Length - Offset);
        Offset = Buffer.Length;
        return remainingSpan;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var newBuffer = new byte[Buffer.Length + data.Length];
        Buffer.AsSpan().CopyTo(newBuffer.AsSpan());
        data.CopyTo(newBuffer.AsSpan(Buffer.Length));
        Buffer = newBuffer;
    }

    public bool ReadBool() => ReadSpan(1)[0] != 0;

    public void WriteBool(bool v) => Write(new[] { v ? (byte)1 : (byte)0 });

    public byte ReadByte() => ReadSpan(1)[0];

    public void WriteByte(byte v)
    {
        unsafe
        {
            Write(new ReadOnlySpan<byte>(&v, 1));
        }
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUShort((ushort)bytes.Length);
        Write(bytes);
    }
    
    public string ReadString()
    {
        var length = ReadUShort();
        var bytes = ReadSpan(length);
        return Encoding.UTF8.GetString(bytes);
    }

    public short ReadShort(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BitConverter.ToInt16(ReadSpan(2).ToArray(), 0) :
        BinaryPrimitives.ReadInt16BigEndian(ReadSpan(2));

    public void WriteShort(short v, Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
            Write(BitConverter.GetBytes(v));
        else
        {
            var buffer = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buffer, v);
            Write(buffer);
        }
    }

    public ushort ReadUShort(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BitConverter.ToUInt16(ReadSpan(2).ToArray(), 0) :
        BinaryPrimitives.ReadUInt16BigEndian(ReadSpan(2));

    public void WriteUShort(ushort v, Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
            Write(BitConverter.GetBytes(v));
        else
        {
            var buffer = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, v);
            Write(buffer);
        }
    }

    public int ReadInt(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BitConverter.ToInt32(ReadSpan(4).ToArray(), 0) :
        BinaryPrimitives.ReadInt32BigEndian(ReadSpan(4));

    public void WriteInt(int v, Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
            Write(BitConverter.GetBytes(v));
        else
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, v);
            Write(buffer);
        }
    }

    public uint ReadUInt(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BitConverter.ToUInt32(ReadSpan(4).ToArray(), 0) :
        BinaryPrimitives.ReadUInt32BigEndian(ReadSpan(4));

    public void WriteUInt(uint v, Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
        {
            Write(BitConverter.GetBytes(v));
        }
        else
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, v);
            Write(buffer);
        }
    }
    public float ReadFloat(Endianess end = Endianess.Big)
    {
        var bytes = ReadSpan(4).ToArray();
        return end == Endianess.Little
            ? BitConverter.ToSingle(bytes, 0)
            : BitConverter.ToSingle(bytes.Reverse().ToArray(), 0);
    }

    public void WriteFloat(float v, Endianess end = Endianess.Big)
    {
        var buffer = BitConverter.GetBytes(v);
        if (end == Endianess.Big)
        {
            Array.Reverse(buffer);
        }
        Write(buffer);
    }

    public double ReadDouble(Endianess end = Endianess.Big)
    {
        var bytes = ReadSpan(8).ToArray();
        return end == Endianess.Little
            ? BitConverter.ToDouble(bytes, 0)
            : BitConverter.ToDouble(bytes.Reverse().ToArray(), 0);
    }

    public void WriteDouble(double v, Endianess end = Endianess.Big)
    {
        var buffer = BitConverter.GetBytes(v);
        if (end == Endianess.Little)
            Write(buffer);
        else
        {
            Array.Reverse(buffer);
            Write(buffer);
        }
    }

    public long ReadLong(Endianess end = Endianess.Big) =>
        end == Endianess.Little ? BitConverter.ToInt64(ReadSpan(8).ToArray(), 0) :
        BinaryPrimitives.ReadInt64BigEndian(ReadSpan(8));

    public void WriteLong(long v, Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
            Write(BitConverter.GetBytes(v));
        else
        {
            var buffer = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buffer, v);
            Write(buffer);
        }
    }

    public ulong ReadULong(Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
            return BitConverter.ToUInt64(ReadSpan(8).ToArray(), 0);
        return BinaryPrimitives.ReadUInt64BigEndian(ReadSpan(8));
    }
    
    public void WriteULong(ulong v, Endianess end = Endianess.Big)
    {
        if (end == Endianess.Little)
            Write(BitConverter.GetBytes(v));
        else
        {
            var buffer = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, v);
            Write(buffer);
        }
    }

    public int ReadUnsignedVarInt()
    {
        var result = 0;
        var shift = 0;
        byte byteRead;
        do
        {
            if (Offset >= Buffer.Length)
                throw new InvalidOperationException("Not enough bytes to read unsigned varint.");

            byteRead = ReadSpan(1)[0];
            result |= (byteRead & 0x7F) << shift;
            shift += 7;
        } while ((byteRead & 0x80) != 0);
        return result;
    }

    public void WriteUnsignedVarInt(int v)
    {
        while (v >= 0x80)
        {
            Write(new[] { (byte)((v & 0x7F) | 0x80) });
            v >>= 7;
        }
        Write(new[] { (byte)v });
    }

    public long ReadUnsignedVarLong()
    {
        long result = 0;
        var shift = 0;
        byte byteRead;
        do
        {
            if (Offset >= Buffer.Length)
                throw new InvalidOperationException("Not enough bytes to read unsigned varlong.");

            byteRead = ReadSpan(1)[0];
            result |= (long)(byteRead & 0x7F) << shift;
            shift += 7;
        } while ((byteRead & 0x80) != 0);
        return result;
    }

    public void WriteUnsignedVarLong(long v)
    {
        while (v >= 0x80)
        {
            Write(new[] { (byte)((v & 0x7F) | 0x80) });
            v >>= 7;
        }
        Write(new[] { (byte)v });
    }

    public int ReadVarInt() => ZigzagDecode(ReadUnsignedVarInt());

    public void WriteVarInt(int v) => WriteUnsignedVarInt(ZigzagEncode(v));

    public long ReadVarLong() => ZigzagDecode(ReadUnsignedVarLong());

    public void WriteVarLong(long v) => WriteUnsignedVarLong(ZigzagEncode(v));

    public byte[] ReadMagic() => ReadSpan(16).ToArray();

    public void WriteMagic() => Write(RakNetServer.MAGIC);

    private static int ZigzagEncode(int value) => (value << 1) ^ (value >> 31);

    private static long ZigzagEncode(long value) => (value << 1) ^ (value >> 63);

    private static int ZigzagDecode(int value) => (value >> 1) ^ -(value & 1);

    private static long ZigzagDecode(long value) => (value >> 1) ^ -(value & 1);

    public void Dispose()
    {
        Buffer = Array.Empty<byte>();
        Offset = 0;
    }
}
