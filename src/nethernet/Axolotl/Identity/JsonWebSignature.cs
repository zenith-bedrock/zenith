using Zenith.NetherNet.Axolotl.Crypto;

namespace Zenith.NetherNet.Axolotl.Identity;

/// <summary>Detached JWS (header..signature) over a payload (port of <c>JsonWebSignature</c>).</summary>
public static class JsonWebSignature
{
    public const string Algorithm = Es384.Algorithm;
    private const string ProtectedHeader = "{\"alg\":\"" + Algorithm + "\"}";

    public static string SignDetached(string payload, ServerIdentity signer)
    {
        var header = Base64Url.Encode(ProtectedHeader);
        return header + ".." + Base64Url.Encode(signer.Sign(header + "." + Base64Url.Encode(payload)));
    }

    public static bool VerifyDetached(string detached, string payload, PublicKey publicKey)
    {
        var parts = detached.Split('.', 4);
        if (parts.Length != 3)
            throw new IdentityException($"Detached signature must consist of exactly three dot-separated parts, got {parts.Length}");
        if (parts[1] != "")
            throw new IdentityException("Detached signature must not carry a payload");

        System.Text.Json.Nodes.JsonObject header;
        byte[] signature;
        try
        {
            header = Json.DecodeObject(Base64Url.DecodeToString(parts[0]), "detached signature header");
            signature = Base64Url.Decode(parts[2]);
        }
        catch (CryptoException e)
        {
            throw new IdentityException("Detached signature is not valid base64url", e);
        }
        if ((header["alg"] as System.Text.Json.Nodes.JsonValue)?.TryGetValue<string>(out var alg) != true || alg != Algorithm)
            throw new IdentityException("Detached signature must use " + Algorithm);
        return publicKey.Verify(parts[0] + "." + Base64Url.Encode(payload), signature);
    }
}
