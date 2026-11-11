namespace Zenith.NetherNet.Axolotl.Crypto;

/// <summary>Minimal DER writer (port of <c>Der</c>).</summary>
public static class Der
{
    private const long MaxLength = 0xffffffffL;

    public static byte[] Encode(int tag, ReadOnlySpan<byte> body)
    {
        var length = Length(body.Length);
        var result = new byte[1 + length.Length + body.Length];
        result[0] = (byte)tag;
        length.CopyTo(result, 1);
        body.CopyTo(result.AsSpan(1 + length.Length));
        return result;
    }

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public static byte[] Sequence(ReadOnlySpan<byte> body) => Encode(0x30, body);

    public static byte[] Integer(ReadOnlySpan<byte> body) => Encode(0x02, body);

    public static byte[] UnsignedInteger(ReadOnlySpan<byte> value)
    {
        var trimmed = value.TrimStart((byte)0);
        if (trimmed.Length == 0 || (trimmed[0] & 0x80) != 0)
        {
            var padded = new byte[trimmed.Length + 1];
            trimmed.CopyTo(padded.AsSpan(1));
            return Integer(padded);
        }
        return Integer(trimmed);
    }

    public static byte[] OctetString(ReadOnlySpan<byte> body) => Encode(0x04, body);

    public static byte[] BitString(ReadOnlySpan<byte> body)
    {
        var prefixed = new byte[body.Length + 1];
        body.CopyTo(prefixed.AsSpan(1));
        return Encode(0x03, prefixed);
    }

    public static byte[] Tagged(int number, ReadOnlySpan<byte> body) => Encode(0xa0 | number, body);

    private static byte[] Length(int length)
    {
        if (length <= 0x7f)
            return [(byte)length];
        if (length > MaxLength)
            throw new ArgumentException($"DER body of {length} bytes is too long to encode");
        var bytes = new[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length }
            .SkipWhile(b => b == 0).ToArray();
        return [(byte)(0x80 | bytes.Length), .. bytes];
    }
}
