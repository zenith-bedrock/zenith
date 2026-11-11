using System.Security.Cryptography;

namespace Zenith.NetherNet.Axolotl.Crypto;

public static class P384
{
    public const int CoordinateSize = 48;
    public const string OpenSslCurveName = "secp384r1";
    public const string JwkCurveName = "P-384";
    public const string CurveOid = "1.3.132.0.34";

    /// <summary>DER AlgorithmIdentifier body: id-ecPublicKey + secp384r1.</summary>
    public static readonly byte[] AlgorithmIdentifier =
        [0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01, 0x06, 0x05, 0x2b, 0x81, 0x04, 0x00, 0x22];

    public static void CheckKey(ECDsa key)
    {
        string? oid;
        try
        {
            oid = key.ExportParameters(false).Curve.Oid?.Value;
        }
        catch (CryptographicException e)
        {
            throw new CryptoException("Key must be an EC key on curve " + OpenSslCurveName, e);
        }
        if (oid != CurveOid)
            throw new CryptoException("Key must be an EC key on curve " + OpenSslCurveName);
    }

    public static byte[] PadCoordinate(ReadOnlySpan<byte> value)
    {
        if (value.Length > CoordinateSize)
            throw new CryptoException($"Curve component of {value.Length} bytes exceeds the {CoordinateSize} byte coordinate size");
        var padded = new byte[CoordinateSize];
        value.CopyTo(padded.AsSpan(CoordinateSize - value.Length));
        return padded;
    }

    public static byte[] EncodeSubjectPublicKeyInfo(byte[] x, byte[] y) =>
        Der.Sequence(Der.Concat(Der.Sequence(AlgorithmIdentifier), Der.BitString(Der.Concat([0x04], x, y))));
}
