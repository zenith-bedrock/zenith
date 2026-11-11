using System.Text.Json.Nodes;
using Zenith.NetherNet.Axolotl.Sdp;

namespace Zenith.NetherNet.Axolotl.Identity;

/// <summary>The <c>a=identity</c> SDP attribute: a provider, a JWT and a signature over the DTLS fingerprints (port of <c>IdentityAssertion</c>).</summary>
public sealed class IdentityAssertion
{
    public const string ProtocolDefault = "default";
    private const string KeyProvider = "idp";
    private const string KeyProviderDomain = "domain";
    private const string KeyProviderProtocol = "protocol";
    private const string KeyAssertion = "assertion";
    private const string KeyToken = "token";
    private const string KeyFingerprintSignature = "fingerprints";

    private readonly string _fingerprintSignature;

    private IdentityAssertion(string domain, string protocol, JsonWebToken token, string fingerprintSignature)
    {
        if (protocol != ProtocolDefault)
            throw new IdentityException($"Identity provider protocol must be {ProtocolDefault}, got {protocol}");
        if (domain == "")
            throw new IdentityException("Identity provider domain must not be empty");
        Domain = domain;
        Protocol = protocol;
        Token = token;
        _fingerprintSignature = fingerprintSignature;
    }

    public string Domain { get; }
    public string Protocol { get; }
    public JsonWebToken Token { get; }

    public static IdentityAssertion Create(string domain, JsonWebToken token, string fingerprintSignature) =>
        new(domain, ProtocolDefault, token, fingerprintSignature);

    private static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static IdentityAssertion Parse(string attributeValue)
    {
        string envelope;
        try
        {
            envelope = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(attributeValue));
        }
        catch (FormatException)
        {
            throw new IdentityException("Identity attribute is not valid base64");
        }
        if (envelope == "")
            throw new IdentityException("Identity attribute is not valid base64");

        var decoded = Json.DecodeObject(envelope, "identity envelope");
        if (decoded[KeyProvider] is not JsonObject idp)
            throw new IdentityException("Identity has no idp object");

        var domain = AsString(idp[KeyProviderDomain]);
        var protocol = AsString(idp[KeyProviderProtocol]);
        if (domain is null || protocol is null)
            throw new IdentityException("Identity provider is missing its domain or protocol");

        var inner = AsString(decoded[KeyAssertion]);
        if (inner is null)
            throw new IdentityException("Identity assertion must be a JSON string");

        var assertion = Json.DecodeObject(inner, "identity assertion");
        var token = AsString(assertion[KeyToken]);
        var fingerprintSignature = AsString(assertion[KeyFingerprintSignature]);
        if (token is null || fingerprintSignature is null)
            throw new IdentityException("Identity assertion is missing its token or fingerprints");

        return new IdentityAssertion(domain, protocol, JsonWebToken.Parse(token), fingerprintSignature);
    }

    public string Encode()
    {
        var inner = new JsonObject
        {
            [KeyFingerprintSignature] = _fingerprintSignature,
            [KeyToken] = Token.ToString(),
        };
        var envelope = new JsonObject
        {
            [KeyAssertion] = Json.Encode(inner),
            [KeyProvider] = new JsonObject
            {
                [KeyProviderDomain] = Domain,
                [KeyProviderProtocol] = Protocol,
            },
        };
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Json.Encode(envelope)));
    }

    public void VerifyFingerprint(Fingerprint fingerprint)
    {
        if (!JsonWebSignature.VerifyDetached(_fingerprintSignature, fingerprint.ToCanonicalPayload(), Token.PublicKey))
            throw new IdentityException("Fingerprint assertion was not signed by the key in the token");
    }
}
