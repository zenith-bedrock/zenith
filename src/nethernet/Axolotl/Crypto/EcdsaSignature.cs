namespace Zenith.NetherNet.Axolotl.Crypto;

/// <summary>Converts ECDSA signatures between ASN.1 DER and the fixed-width JOSE form (port of <c>EcdsaSignature</c>).</summary>
public static class EcdsaSignature
{
    private const byte Asn1IntegerTag = 0x02;
    private const byte Asn1SequenceTag = 0x30;
    private const int Asn1LongFormFlag = 0x80;

    public static byte[] DerToJose(ReadOnlySpan<byte> der)
    {
        const int coordinateSize = P384.CoordinateSize;
        if (der.Length < 2 || der[0] != Asn1SequenceTag)
            throw new CryptoException("Signature does not start with an ASN.1 SEQUENCE tag");

        int length = der[1];
        if ((length & Asn1LongFormFlag) != 0)
            throw new CryptoException("Long-form DER length is not permitted for ECDSA signatures");
        if (der.Length != length + 2)
            throw new CryptoException($"Signature length byte {length} does not match actual length {der.Length - 2}");

        var offset = 2;
        var r = ReadInteger(der, ref offset, coordinateSize);
        var s = ReadInteger(der, ref offset, coordinateSize);
        if (offset != der.Length)
            throw new CryptoException("Signature has trailing data after its two integers");
        return [.. r, .. s];
    }

    private static byte[] ReadInteger(ReadOnlySpan<byte> der, ref int offset, int coordinateSize)
    {
        if (offset + 2 > der.Length)
            throw new CryptoException("Signature ended before an expected integer");
        var tag = der[offset];
        if (tag != Asn1IntegerTag)
            throw new CryptoException($"Expected an ASN.1 INTEGER tag, got {tag:x2}");
        int length = der[offset + 1];
        if ((length & Asn1LongFormFlag) != 0)
            throw new CryptoException("Integer uses a long-form DER length");
        if (length < 1 || length > coordinateSize + 1)
            throw new CryptoException($"Integer length {length} does not fit a {coordinateSize} byte coordinate");
        offset += 2;
        if (offset + length > der.Length)
            throw new CryptoException("Signature ended in the middle of an integer");
        var value = der.Slice(offset, length).TrimStart((byte)0);
        offset += length;
        if (value.Length > coordinateSize)
            throw new CryptoException($"Integer length exceeds coordinate size of {coordinateSize} bytes");
        var padded = new byte[coordinateSize];
        value.CopyTo(padded.AsSpan(coordinateSize - value.Length));
        return padded;
    }

    public static byte[] JoseToDer(ReadOnlySpan<byte> jose)
    {
        const int coordinateSize = P384.CoordinateSize;
        if (jose.Length != coordinateSize * 2)
            throw new CryptoException($"Signature must be {coordinateSize * 2} bytes, got {jose.Length}");
        var r = Der.UnsignedInteger(jose[..coordinateSize]);
        var s = Der.UnsignedInteger(jose[coordinateSize..]);
        return Der.Sequence(Der.Concat(r, s));
    }
}
