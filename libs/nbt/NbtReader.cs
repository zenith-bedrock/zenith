using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Zenith.Nbt;

/// <summary>
/// Zero-copy reader over a span. Encoding selected at construction (LE disk vs network).
/// </summary>
public ref struct NbtReader
{
    private ReadOnlySpan<byte> _data;
    private int _offset;
    private readonly NbtEncoding _encoding;
    private int _depth;

    public NbtReader(ReadOnlySpan<byte> data, NbtEncoding encoding)
    {
        _data = data;
        _offset = 0;
        _encoding = encoding;
        _depth = 0;
    }

    public int BytesConsumed => _offset;

    /// <summary>Reads type + name + payload (named tag). Root of block_palette / PropertyData.</summary>
    public NbtNamedTag ReadNamedTag()
    {
        var type = (NbtType)ReadU8();
        if (type == NbtType.End)
            throw new NbtException("Unexpected TAG_End at named tag.");
        var name = ReadString();
        var tag = ReadPayload(type);
        return new NbtNamedTag(name, tag);
    }

    private NbtTag ReadPayload(NbtType type)
    {
        return type switch
        {
            NbtType.Byte => NbtTag.Byte(ReadU8()),
            NbtType.Short => NbtTag.Short(ReadShortNum()),
            NbtType.Int => NbtTag.Int(ReadInt()),
            NbtType.Long => NbtTag.Long(ReadLong()),
            NbtType.Float => NbtTag.Float(ReadFloatNum()),
            NbtType.Double => NbtTag.Double(ReadDoubleNum()),
            NbtType.ByteArray => NbtTag.ByteArray(ReadByteArray()),
            NbtType.String => NbtTag.String(ReadString()),
            NbtType.List => NbtTag.List(ReadList()),
            NbtType.Compound => NbtTag.Compound(ReadCompound()),
            NbtType.IntArray => NbtTag.IntArray(ReadIntArray()),
            NbtType.LongArray => NbtTag.LongArray(ReadLongArray()),
            _ => throw new NbtException($"Unknown NBT type {(byte)type}.")
        };
    }

    private NbtCompound ReadCompound()
    {
        EnterDepth();
        var compound = new NbtCompound();
        var entries = 0;
        while (true)
        {
            var type = (NbtType)ReadU8();
            if (type == NbtType.End)
                break;
            if (++entries > NbtLimits.MaxCompoundEntries)
                throw new NbtException($"Compound exceeds MaxCompoundEntries ({NbtLimits.MaxCompoundEntries}).");
            var name = ReadString();
            compound.Set(name, ReadPayload(type));
        }

        LeaveDepth();
        return compound;
    }

    private NbtList ReadList()
    {
        EnterDepth();
        var elementType = (NbtType)ReadU8();
        var count = ReadLength();
        if (elementType == NbtType.End && count != 0)
            throw new NbtException("TAG_End list with non-zero length.");
        var list = new NbtList(elementType == NbtType.End ? NbtType.End : elementType);
        if (elementType != NbtType.End)
        {
            for (var i = 0; i < count; i++)
                list.Add(ReadPayload(elementType));
        }

        LeaveDepth();
        return list;
    }

    private byte[] ReadByteArray()
    {
        var len = ReadLength();
        EnsureArrayLength(len);
        Need(len);
        var arr = _data.Slice(_offset, len).ToArray();
        _offset += len;
        return arr;
    }

    private int[] ReadIntArray()
    {
        var len = ReadLength();
        EnsureArrayLength(len);
        var arr = new int[len];
        for (var i = 0; i < len; i++)
            arr[i] = ReadInt();
        return arr;
    }

    private long[] ReadLongArray()
    {
        var len = ReadLength();
        EnsureArrayLength(len);
        var arr = new long[len];
        for (var i = 0; i < len; i++)
            arr[i] = ReadLong();
        return arr;
    }

    private string ReadString()
    {
        var len = ReadStringLength();
        if (len < 0)
            throw new NbtException($"Negative string length {len}.");
        if (len > NbtLimits.MaxStringLength)
            throw new NbtException($"String length {len} exceeds MaxStringLength ({NbtLimits.MaxStringLength}).");
        Need(len);
        var s = Encoding.UTF8.GetString(_data.Slice(_offset, len));
        _offset += len;
        return s;
    }

    private int ReadLength()
    {
        var len = ReadInt();
        if (len < 0)
            throw new NbtException($"Negative array/list length {len}.");
        return len;
    }

    private void EnsureArrayLength(int len)
    {
        if (len > NbtLimits.MaxArrayLength)
            throw new NbtException($"Array length {len} exceeds MaxArrayLength ({NbtLimits.MaxArrayLength}).");
    }

    private int ReadInt() => _encoding switch
    {
        NbtEncoding.Network => ReadZigZagVarInt(),
        NbtEncoding.BigEndian => ReadI32BE(),
        _ => ReadI32()
    };

    private long ReadLong() => _encoding switch
    {
        NbtEncoding.Network => ReadZigZagVarLong(),
        NbtEncoding.BigEndian => ReadI64BE(),
        _ => ReadI64()
    };

    private short ReadShortNum() =>
        _encoding == NbtEncoding.BigEndian ? ReadI16BE() : ReadI16();

    private float ReadFloatNum() =>
        _encoding == NbtEncoding.BigEndian ? ReadF32BE() : ReadF32();

    private double ReadDoubleNum() =>
        _encoding == NbtEncoding.BigEndian ? ReadF64BE() : ReadF64();

    private void EnterDepth()
    {
        if (++_depth > NbtLimits.MaxDepth)
            throw new NbtException($"NBT nesting exceeds MaxDepth ({NbtLimits.MaxDepth}).");
    }

    private void LeaveDepth() => _depth--;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Need(int n)
    {
        if (_offset + n > _data.Length)
            throw new NbtException($"Unexpected end of NBT buffer at offset {_offset} (need {n}).");
    }

    private byte ReadU8()
    {
        Need(1);
        return _data[_offset++];
    }

    private short ReadI16()
    {
        Need(2);
        var v = BinaryPrimitives.ReadInt16LittleEndian(_data.Slice(_offset));
        _offset += 2;
        return v;
    }

    private short ReadI16BE()
    {
        Need(2);
        var v = BinaryPrimitives.ReadInt16BigEndian(_data.Slice(_offset));
        _offset += 2;
        return v;
    }

    private int ReadU16()
    {
        Need(2);
        var v = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_offset));
        _offset += 2;
        return v;
    }

    private int ReadU16BE()
    {
        Need(2);
        var v = BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(_offset));
        _offset += 2;
        return v;
    }

    private int ReadI32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_offset));
        _offset += 4;
        return v;
    }

    private int ReadI32BE()
    {
        Need(4);
        var v = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(_offset));
        _offset += 4;
        return v;
    }

    private long ReadI64()
    {
        Need(8);
        var v = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_offset));
        _offset += 8;
        return v;
    }

    private long ReadI64BE()
    {
        Need(8);
        var v = BinaryPrimitives.ReadInt64BigEndian(_data.Slice(_offset));
        _offset += 8;
        return v;
    }

    private float ReadF32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(_offset));
        _offset += 4;
        return v;
    }

    private float ReadF32BE()
    {
        Need(4);
        var v = BinaryPrimitives.ReadSingleBigEndian(_data.Slice(_offset));
        _offset += 4;
        return v;
    }

    private double ReadF64()
    {
        Need(8);
        var v = BinaryPrimitives.ReadDoubleLittleEndian(_data.Slice(_offset));
        _offset += 8;
        return v;
    }

    private double ReadF64BE()
    {
        Need(8);
        var v = BinaryPrimitives.ReadDoubleBigEndian(_data.Slice(_offset));
        _offset += 8;
        return v;
    }

    private int ReadStringLength() => _encoding switch
    {
        NbtEncoding.Network => (int)ReadUnsignedVarIntU32(),
        NbtEncoding.BigEndian => ReadU16BE(),
        _ => ReadU16()
    };

    private int ReadZigZagVarInt()
    {
        var ux = ReadUnsignedVarIntU32();
        return (int)((ux >> 1) ^ (uint)-(int)(ux & 1));
    }

    private long ReadZigZagVarLong()
    {
        var ux = ReadUnsignedVarLongU64();
        return (long)((ux >> 1) ^ (ulong)-(long)(ux & 1));
    }

    private uint ReadUnsignedVarIntU32()
    {
        uint result = 0;
        var shift = 0;
        byte b;
        do
        {
            if (shift >= 35)
                throw new NbtException("Malformed unsigned varint.");
            Need(1);
            b = _data[_offset++];
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }

    private ulong ReadUnsignedVarLongU64()
    {
        ulong result = 0;
        var shift = 0;
        byte b;
        do
        {
            if (shift >= 70)
                throw new NbtException("Malformed unsigned varlong.");
            Need(1);
            b = _data[_offset++];
            result |= (ulong)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }
}

/// <summary>Decode helper returning tag + bytes consumed.</summary>
public readonly record struct NbtDecodeResult(NbtNamedTag Root, int BytesConsumed);

public static class NbtCodec
{
    public static NbtDecodeResult Decode(ReadOnlySpan<byte> data, NbtEncoding encoding)
    {
        var reader = new NbtReader(data, encoding);
        var root = reader.ReadNamedTag();
        return new NbtDecodeResult(root, reader.BytesConsumed);
    }

    public static byte[] Encode(NbtNamedTag root, NbtEncoding encoding)
    {
        var writer = new NbtWriter(encoding);
        writer.WriteNamedTag(root.Name, root.Tag);
        return writer.ToArray();
    }
}
