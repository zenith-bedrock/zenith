namespace Zenith.LevelDB;

/// <summary>Unsigned LEB128, the length encoding inside table blocks.</summary>
static class Varint
{
    public const int MaxLength = 5;

    public static int Write(Span<byte> destination, uint value)
    {
        var i = 0;
        while (value >= 0x80)
        {
            destination[i++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[i++] = (byte)value;
        return i;
    }

    /// <summary>Reads one value at <paramref name="position"/>; false when the bytes end or overflow 32 bits.</summary>
    public static bool TryRead(ReadOnlySpan<byte> source, ref int position, out uint value)
    {
        value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            if (position >= source.Length) return false;
            var b = source[position++];
            if (shift == 28 && b > 0x0F) return false;
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }

        return false;
    }
}
