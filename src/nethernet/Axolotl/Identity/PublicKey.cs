using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Zenith.NetherNet.Axolotl.Crypto;

namespace Zenith.NetherNet.Axolotl.Identity;

/// <summary>An ECDSA P-384 public key (port of <c>PublicKey</c>).</summary>
public sealed class PublicKey
{
    private readonly ECDsa _key;
    private readonly byte[] _der;

    private PublicKey(ECDsa key, byte[] der)
    {
        _key = key;
        _der = der;
    }

    /// <summary>Accepts either a Base64 DER SubjectPublicKeyInfo string or a P-384 JWK object.</summary>
    public static PublicKey FromClaim(JsonNode? value)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var s))
            return FromBase64Der(s);
        if (value is JsonObject o)
            return FromJwk(o);
        throw new IdentityException("Public key claim must be a base64 string or a JWK object");
    }

    public static PublicKey FromBase64Der(string base64)
    {
        byte[] der;
        try
        {
            der = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new IdentityException("Public key is not valid base64");
        }
        if (der.Length == 0)
            throw new IdentityException("Public key is not valid base64");
        return FromDer(der);
    }

    public static PublicKey FromJwk(JsonObject jwk)
    {
        if (jwk["kty"]?.GetValueKind() != System.Text.Json.JsonValueKind.String || (string?)jwk["kty"] != "EC")
            throw new IdentityException("Public key JWK must have kty EC");
        if ((jwk["crv"] as JsonValue)?.TryGetValue<string>(out var crv) != true || crv != P384.JwkCurveName)
            throw new IdentityException("Public key JWK must use curve " + P384.JwkCurveName);
        if ((jwk["x"] as JsonValue)?.TryGetValue<string>(out var x) != true
            || (jwk["y"] as JsonValue)?.TryGetValue<string>(out var y) != true)
            throw new IdentityException("Public key JWK is missing its coordinates");

        byte[] xBytes, yBytes;
        try
        {
            xBytes = Base64Url.Decode(x!);
            yBytes = Base64Url.Decode(y!);
        }
        catch (CryptoException e)
        {
            throw new IdentityException("Public key JWK coordinates are not valid base64url", e);
        }
        if (xBytes.Length != P384.CoordinateSize || yBytes.Length != P384.CoordinateSize)
            throw new IdentityException($"Public key JWK coordinates must be {P384.CoordinateSize} bytes each");
        return FromDer(P384.EncodeSubjectPublicKeyInfo(xBytes, yBytes));
    }

    public static PublicKey FromDer(byte[] der)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(der, out _);
        }
        catch (CryptographicException e)
        {
            key.Dispose();
            throw new IdentityException("The public key was rejected: " + e.Message, e);
        }
        try
        {
            P384.CheckKey(key);
        }
        catch (CryptoException e)
        {
            key.Dispose();
            throw new IdentityException(e.Message, e);
        }
        return new PublicKey(key, der);
    }

    public static PublicKey FromEcdsa(ECDsa key)
    {
        try
        {
            return FromDer(key.ExportSubjectPublicKeyInfo());
        }
        catch (CryptographicException e)
        {
            throw new IdentityException("Failed to extract public key: " + e.Message, e);
        }
    }

    public bool Verify(string input, byte[] signature) => Verify(System.Text.Encoding.UTF8.GetBytes(input), signature);

    public bool Verify(byte[] input, byte[] signature)
    {
        try
        {
            return Es384.Verify(input, signature, _key);
        }
        catch (CryptoException e)
        {
            throw new IdentityException(e.Message, e);
        }
    }

    public string ToBase64Der() => Convert.ToBase64String(_der);

    public byte[] GetDigest() => SHA256.HashData(_der);

    public bool Equals(PublicKey other) => CryptographicOperations.FixedTimeEquals(_der, other._der);
}
