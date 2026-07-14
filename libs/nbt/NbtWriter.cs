using System.Buffers.Binary;
using System.Text;

namespace Zenith.Nbt;

/// <summary>Growing buffer writer for named NBT tags (LE or network encoding).</summary>
public sealed class NbtWriter
{
    private readonly NbtEncoding _encoding;
    private byte[] _buffer = new byte[256];
    private int _length;

    public NbtWriter(NbtEncoding encoding) => _encoding = encoding;

    public int Length => _length;

    public byte[] ToArray()
    {
        var result = new byte[_length];
        Buffer.BlockCopy(_buffer, 0, result, 0, _length);
        return result;
    }

    public void WriteNamedTag(string name, NbtTag tag)
    {
        WriteU8((byte)tag.Type);
        WriteString(name);
        WritePayload(tag);
    }

    private void WritePayload(NbtTag tag)
    {
        switch (tag.Type)
        {
            case NbtType.Byte:
                WriteU8((byte)tag.AsByte());
                break;
            case NbtType.Short:
                WriteShortNum(tag.AsShort());
                break;
            case NbtType.Int:
                WriteInt(tag.AsInt());
                break;
            case NbtType.Long:
                WriteLong(tag.AsLong());
                break;
            case NbtType.Float:
                WriteFloatNum(tag.AsFloat());
                break;
            case NbtType.Double:
                WriteDoubleNum(tag.AsDouble());
                break;
            case NbtType.ByteArray:
            {
                var arr = tag.AsByteArray();
                WriteLength(arr.Length);
                WriteRaw(arr);
                break;
            }
            case NbtType.String:
                WriteString(tag.AsString());
                break;
            case NbtType.List:
            {
                var list = tag.AsList();
                WriteU8((byte)list.ElementType);
                WriteLength(list.Count);
                for (var i = 0; i < list.Count; i++)
                    WritePayload(list[i]);
                break;
            }
            case NbtType.Compound:
            {
                var compound = tag.AsCompound();
                foreach (var entry in compound.Entries)
                {
                    WriteU8((byte)entry.Value.Type);
                    WriteString(entry.Key);
                    WritePayload(entry.Value);
                }

                WriteU8((byte)NbtType.End);
                break;
            }
            case NbtType.IntArray:
            {
                var arr = tag.AsIntArray();
                WriteLength(arr.Length);
                foreach (var v in arr)
                    WriteInt(v);
                break;
            }
            case NbtType.LongArray:
            {
                var arr = tag.AsLongArray();
                WriteLength(arr.Length);
                foreach (var v in arr)
                    WriteLong(v);
                break;
            }
            default:
                throw new NbtException($"Cannot write NBT type {tag.Type}.");
        }
    }

    private void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > NbtLimits.MaxStringLength)
            throw new NbtException($"String length {bytes.Length} exceeds MaxStringLength.");
        switch (_encoding)
        {
            case NbtEncoding.Network:
                WriteUnsignedVarInt(bytes.Length);
                break;
            case NbtEncoding.BigEndian:
                if (bytes.Length > ushort.MaxValue)
                    throw new NbtException("Big-endian NBT string length exceeds u16.");
                WriteU16BE((ushort)bytes.Length);
                break;
            default:
                if (bytes.Length > ushort.MaxValue)
                    throw new NbtException("Little-endian NBT string length exceeds u16.");
                WriteU16((ushort)bytes.Length);
                break;
        }

        WriteRaw(bytes);
    }

    private void WriteLength(int length)
    {
        if (length < 0)
            throw new NbtException("Negative length.");
        WriteInt(length);
    }

    private void WriteInt(int value)
    {
        switch (_encoding)
        {
            case NbtEncoding.Network:
                WriteZigZagVarInt(value);
                break;
            case NbtEncoding.BigEndian:
                WriteI32BE(value);
                break;
            default:
                WriteI32(value);
                break;
        }
    }

    private void WriteLong(long value)
    {
        switch (_encoding)
        {
            case NbtEncoding.Network:
                WriteZigZagVarLong(value);
                break;
            case NbtEncoding.BigEndian:
                WriteI64BE(value);
                break;
            default:
                WriteI64(value);
                break;
        }
    }

    private void WriteShortNum(short value)
    {
        if (_encoding == NbtEncoding.BigEndian)
            WriteI16BE(value);
        else
            WriteI16(value);
    }

    private void WriteFloatNum(float value)
    {
        if (_encoding == NbtEncoding.BigEndian)
            WriteF32BE(value);
        else
            WriteF32(value);
    }

    private void WriteDoubleNum(double value)
    {
        if (_encoding == NbtEncoding.BigEndian)
            WriteF64BE(value);
        else
            WriteF64(value);
    }

    private void Ensure(int additional)
    {
        var required = _length + additional;
        if (required <= _buffer.Length) return;
        var newCap = Math.Max(_buffer.Length * 2, required);
        var next = new byte[newCap];
        Buffer.BlockCopy(_buffer, 0, next, 0, _length);
        _buffer = next;
    }

    private void WriteU8(byte v)
    {
        Ensure(1);
        _buffer[_length++] = v;
    }

    private void WriteRaw(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    private void WriteI16(short v)
    {
        Ensure(2);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_length), v);
        _length += 2;
    }

    private void WriteI16BE(short v)
    {
        Ensure(2);
        BinaryPrimitives.WriteInt16BigEndian(_buffer.AsSpan(_length), v);
        _length += 2;
    }

    private void WriteU16(ushort v)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), v);
        _length += 2;
    }

    private void WriteU16BE(ushort v)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_length), v);
        _length += 2;
    }

    private void WriteI32(int v)
    {
        Ensure(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_length), v);
        _length += 4;
    }

    private void WriteI32BE(int v)
    {
        Ensure(4);
        BinaryPrimitives.WriteInt32BigEndian(_buffer.AsSpan(_length), v);
        _length += 4;
    }

    private void WriteI64(long v)
    {
        Ensure(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_length), v);
        _length += 8;
    }

    private void WriteI64BE(long v)
    {
        Ensure(8);
        BinaryPrimitives.WriteInt64BigEndian(_buffer.AsSpan(_length), v);
        _length += 8;
    }

    private void WriteF32(float v)
    {
        Ensure(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_length), v);
        _length += 4;
    }

    private void WriteF32BE(float v)
    {
        Ensure(4);
        BinaryPrimitives.WriteSingleBigEndian(_buffer.AsSpan(_length), v);
        _length += 4;
    }

    private void WriteF64(double v)
    {
        Ensure(8);
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer.AsSpan(_length), v);
        _length += 8;
    }

    private void WriteF64BE(double v)
    {
        Ensure(8);
        BinaryPrimitives.WriteDoubleBigEndian(_buffer.AsSpan(_length), v);
        _length += 8;
    }

    private void WriteUnsignedVarInt(int v)
    {
        var value = (uint)v;
        while (value >= 0x80)
        {
            WriteU8((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        WriteU8((byte)value);
    }

    private void WriteUnsignedVarLong(ulong value)
    {
        while (value >= 0x80)
        {
            WriteU8((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        WriteU8((byte)value);
    }

    private void WriteZigZagVarInt(int value) =>
        WriteUnsignedVarInt((value << 1) ^ (value >> 31));

    private void WriteZigZagVarLong(long value) =>
        WriteUnsignedVarLong((ulong)((value << 1) ^ (value >> 63)));
}
