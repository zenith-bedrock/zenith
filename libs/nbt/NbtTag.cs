using System.Runtime.CompilerServices;

namespace Zenith.Nbt;

/// <summary>
/// Named root tag (type+name+payload on the wire).
/// </summary>
public readonly record struct NbtNamedTag(string Name, NbtTag Tag);

/// <summary>
/// Discriminated NBT value. Scalars live in fields (no boxing);
/// compound/list/arrays/string use heap references.
/// </summary>
public readonly struct NbtTag : IEquatable<NbtTag>
{
    private readonly NbtType _type;
    private readonly long _raw;
    private readonly object? _ref;

    private NbtTag(NbtType type, long raw, object? reference)
    {
        _type = type;
        _raw = raw;
        _ref = reference;
    }

    public NbtType Type => _type;

    public static NbtTag Byte(sbyte value) => new(NbtType.Byte, value, null);
    public static NbtTag Byte(byte value) => new(NbtType.Byte, (sbyte)value, null);
    public static NbtTag Short(short value) => new(NbtType.Short, value, null);
    public static NbtTag Int(int value) => new(NbtType.Int, value, null);
    public static NbtTag Long(long value) => new(NbtType.Long, value, null);
    public static NbtTag Float(float value) => new(NbtType.Float, BitConverter.SingleToInt32Bits(value), null);
    public static NbtTag Double(double value) => new(NbtType.Double, BitConverter.DoubleToInt64Bits(value), null);
    public static NbtTag String(string value) => new(NbtType.String, 0, value ?? throw new ArgumentNullException(nameof(value)));
    public static NbtTag ByteArray(byte[] value) => new(NbtType.ByteArray, 0, value ?? throw new ArgumentNullException(nameof(value)));
    public static NbtTag IntArray(int[] value) => new(NbtType.IntArray, 0, value ?? throw new ArgumentNullException(nameof(value)));
    public static NbtTag LongArray(long[] value) => new(NbtType.LongArray, 0, value ?? throw new ArgumentNullException(nameof(value)));
    public static NbtTag List(NbtList value) => new(NbtType.List, 0, value ?? throw new ArgumentNullException(nameof(value)));
    public static NbtTag Compound(NbtCompound value) => new(NbtType.Compound, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public bool TryGetByte(out sbyte value)
    {
        if (_type != NbtType.Byte) { value = 0; return false; }
        value = (sbyte)_raw;
        return true;
    }

    public bool TryGetShort(out short value)
    {
        if (_type != NbtType.Short) { value = 0; return false; }
        value = (short)_raw;
        return true;
    }

    public bool TryGetInt(out int value)
    {
        if (_type != NbtType.Int) { value = 0; return false; }
        value = (int)_raw;
        return true;
    }

    public bool TryGetLong(out long value)
    {
        if (_type != NbtType.Long) { value = 0; return false; }
        value = _raw;
        return true;
    }

    public bool TryGetFloat(out float value)
    {
        if (_type != NbtType.Float) { value = 0; return false; }
        value = BitConverter.Int32BitsToSingle((int)_raw);
        return true;
    }

    public bool TryGetDouble(out double value)
    {
        if (_type != NbtType.Double) { value = 0; return false; }
        value = BitConverter.Int64BitsToDouble(_raw);
        return true;
    }

    public bool TryGetString(out string value)
    {
        if (_type != NbtType.String || _ref is not string s) { value = ""; return false; }
        value = s;
        return true;
    }

    public bool TryGetByteArray(out byte[] value)
    {
        if (_type != NbtType.ByteArray || _ref is not byte[] a) { value = Array.Empty<byte>(); return false; }
        value = a;
        return true;
    }

    public bool TryGetIntArray(out int[] value)
    {
        if (_type != NbtType.IntArray || _ref is not int[] a) { value = Array.Empty<int>(); return false; }
        value = a;
        return true;
    }

    public bool TryGetLongArray(out long[] value)
    {
        if (_type != NbtType.LongArray || _ref is not long[] a) { value = Array.Empty<long>(); return false; }
        value = a;
        return true;
    }

    public bool TryGetList(out NbtList value)
    {
        if (_type != NbtType.List || _ref is not NbtList list) { value = null!; return false; }
        value = list;
        return true;
    }

    public bool TryGetCompound(out NbtCompound value)
    {
        if (_type != NbtType.Compound || _ref is not NbtCompound c) { value = null!; return false; }
        value = c;
        return true;
    }

    public sbyte AsByte() => TryGetByte(out var v) ? v : throw TypeMismatch(NbtType.Byte);
    public short AsShort() => TryGetShort(out var v) ? v : throw TypeMismatch(NbtType.Short);
    public int AsInt() => TryGetInt(out var v) ? v : throw TypeMismatch(NbtType.Int);
    public long AsLong() => TryGetLong(out var v) ? v : throw TypeMismatch(NbtType.Long);
    public float AsFloat() => TryGetFloat(out var v) ? v : throw TypeMismatch(NbtType.Float);
    public double AsDouble() => TryGetDouble(out var v) ? v : throw TypeMismatch(NbtType.Double);
    public string AsString() => TryGetString(out var v) ? v : throw TypeMismatch(NbtType.String);
    public byte[] AsByteArray() => TryGetByteArray(out var v) ? v : throw TypeMismatch(NbtType.ByteArray);
    public int[] AsIntArray() => TryGetIntArray(out var v) ? v : throw TypeMismatch(NbtType.IntArray);
    public long[] AsLongArray() => TryGetLongArray(out var v) ? v : throw TypeMismatch(NbtType.LongArray);
    public NbtList AsList() => TryGetList(out var v) ? v : throw TypeMismatch(NbtType.List);
    public NbtCompound AsCompound() => TryGetCompound(out var v) ? v : throw TypeMismatch(NbtType.Compound);

    /// <summary>Compound indexer: <c>tag["Name"]</c>.</summary>
    public NbtTag this[string name] => AsCompound()[name];

    /// <summary>List indexer: <c>tag[i]</c>.</summary>
    public NbtTag this[int index] => AsList()[index];

    private NbtException TypeMismatch(NbtType expected) =>
        new($"Expected NBT {expected}, got {_type}.");

    public bool Equals(NbtTag other)
    {
        if (_type != other._type) return false;
        return _type switch
        {
            NbtType.Byte or NbtType.Short or NbtType.Int or NbtType.Long or NbtType.Float or NbtType.Double
                => _raw == other._raw,
            NbtType.String => string.Equals((string?)_ref, (string?)other._ref, StringComparison.Ordinal),
            NbtType.ByteArray => _ref is byte[] a && other._ref is byte[] b && a.AsSpan().SequenceEqual(b),
            NbtType.IntArray => _ref is int[] ia && other._ref is int[] ib && ia.AsSpan().SequenceEqual(ib),
            NbtType.LongArray => _ref is long[] la && other._ref is long[] lb && la.AsSpan().SequenceEqual(lb),
            NbtType.List => ListEquals((NbtList)_ref!, (NbtList)other._ref!),
            NbtType.Compound => CompoundEquals((NbtCompound)_ref!, (NbtCompound)other._ref!),
            _ => false
        };
    }

    private static bool ListEquals(NbtList a, NbtList b)
    {
        if (a.ElementType != b.ElementType || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }

        return true;
    }

    private static bool CompoundEquals(NbtCompound a, NbtCompound b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Entries.Count; i++)
        {
            var ea = a.Entries[i];
            var eb = b.Entries[i];
            if (!string.Equals(ea.Key, eb.Key, StringComparison.Ordinal) || !ea.Value.Equals(eb.Value))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is NbtTag t && Equals(t);
    public override int GetHashCode() => HashCode.Combine((int)_type, _raw, _ref);
    public static bool operator ==(NbtTag left, NbtTag right) => left.Equals(right);
    public static bool operator !=(NbtTag left, NbtTag right) => !left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal object? GetReference() => _ref;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long GetRaw() => _raw;
}
