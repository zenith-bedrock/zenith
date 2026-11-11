using System.Text.Json;
using System.Text.Json.Nodes;
using Zenith.NetherNet.Axolotl.Crypto;

namespace Zenith.NetherNet.Axolotl.Identity;

/// <summary>A compact JWT carrying the peer public key in <c>cpk</c> (port of <c>JsonWebToken</c>).</summary>
public sealed class JsonWebToken
{
    public const string ClaimPublicKey = "cpk";
    public const string ClaimExpiry = "exp";
    public const string ClaimNotBefore = "nbf";
    public const string ClaimIssuedAt = "iat";
    public const string HeaderAlgorithm = "alg";
    public const string HeaderPublicKey = "x5u";
    public const int DefaultLeewaySeconds = 60;

    private readonly string _compact;
    private readonly string _signingInput;
    private readonly byte[] _signature;

    private JsonWebToken(string compact, JsonObject header, JsonObject claims, PublicKey publicKey, string signingInput, byte[] signature)
    {
        _compact = compact;
        Header = header;
        Claims = claims;
        PublicKey = publicKey;
        _signingInput = signingInput;
        _signature = signature;
    }

    public JsonObject Header { get; }
    public JsonObject Claims { get; }
    public PublicKey PublicKey { get; }

    public static JsonWebToken Parse(string compact)
    {
        var parts = compact.Split('.', 4);
        if (parts.Length != 3)
            throw new IdentityException($"Expected 3 parts in JWT, got {parts.Length}");

        JsonObject header, claims;
        byte[] signature;
        try
        {
            header = Json.DecodeObject(Base64Url.DecodeToString(parts[0]), "token header");
            claims = Json.DecodeObject(Base64Url.DecodeToString(parts[1]), "token claims");
            signature = Base64Url.Decode(parts[2]);
        }
        catch (CryptoException e)
        {
            throw new IdentityException("Token is not valid base64url", e);
        }
        if (!claims.ContainsKey(ClaimPublicKey) || claims[ClaimPublicKey] is null)
            throw new IdentityException("Token has no " + ClaimPublicKey + " claim");

        return new JsonWebToken(compact, header, claims, PublicKey.FromClaim(claims[ClaimPublicKey]),
            parts[0] + "." + parts[1], signature);
    }

    public static JsonWebToken Create(JsonObject header, JsonObject claims, ServerIdentity signer)
    {
        header[HeaderAlgorithm] = JsonWebSignature.Algorithm;
        var signingInput = Base64Url.Encode(Json.Encode(header)) + "." + Base64Url.Encode(Json.Encode(claims));
        return Parse(signingInput + "." + Base64Url.Encode(signer.Sign(signingInput)));
    }

    public override string ToString() => _compact;

    public string? Algorithm =>
        (Header[HeaderAlgorithm] as JsonValue)?.TryGetValue<string>(out var a) == true ? a : null;

    public bool IsSignedBy(PublicKey key)
    {
        if (Algorithm != JsonWebSignature.Algorithm)
            throw new IdentityException("Token must be signed with " + JsonWebSignature.Algorithm);
        return key.Verify(_signingInput, _signature);
    }

    public void CheckTimestamps(long now, int leewaySeconds = DefaultLeewaySeconds)
    {
        var notBefore = ReadTimestamp(ClaimNotBefore);
        if (notBefore is not null && now + leewaySeconds < notBefore)
            throw new IdentityException("Token is not valid yet");

        var issuedAt = ReadTimestamp(ClaimIssuedAt);
        if (issuedAt is not null && now + leewaySeconds < issuedAt)
            throw new IdentityException("Token was issued in the future");

        var expiry = ReadTimestamp(ClaimExpiry);
        if (expiry is not null && now - leewaySeconds >= expiry)
            throw new IdentityException("Token has expired");
    }

    private long? ReadTimestamp(string claim)
    {
        var node = Claims[claim];
        if (node is null)
            return null;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number)
        {
            var el = v.GetValue<JsonElement>();
            if (el.TryGetInt64(out var l))
                return l;
            return (long)el.GetDouble();
        }
        throw new IdentityException($"Token claim {claim} is not a number");
    }
}
