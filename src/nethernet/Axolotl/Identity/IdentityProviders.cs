using System.Text.Json.Nodes;
using Zenith.NetherNet.Axolotl.Sdp;

namespace Zenith.NetherNet.Axolotl.Identity;

public interface IIdentityProvider
{
    IdentityAssertion Issue(Fingerprint localFingerprint);
}

public interface ITokenVerifier
{
    void Check(JsonWebToken token);
}

/// <summary>The authenticated remote peer extracted from an accepted offer (port of <c>PeerIdentity</c>).</summary>
public sealed record PeerIdentity(PublicKey PublicKey, string ProviderDomain, JsonWebToken Token);

/// <summary>Accepts tokens signed by the key they carry (port of <c>SelfSignedTokenVerifier</c>).</summary>
public sealed class SelfSignedTokenVerifier : ITokenVerifier
{
    public void Check(JsonWebToken token)
    {
        if (!token.IsSignedBy(token.PublicKey))
            throw new IdentityException("Token is not signed by the key it carries");
    }
}

/// <summary>Issues short-lived self-signed assertions (port of <c>SelfSignedIdentityProvider</c>).</summary>
public sealed class SelfSignedIdentityProvider : IIdentityProvider
{
    public const string DefaultDomain = "self";
    public const int DefaultTokenLifetime = 60;

    private readonly ServerIdentity _identity;
    private readonly string _domain;
    private readonly int _tokenLifetimeSeconds;

    public SelfSignedIdentityProvider(ServerIdentity identity, string domain = DefaultDomain, int tokenLifetimeSeconds = DefaultTokenLifetime)
    {
        if (domain == "")
            throw new ArgumentException("Identity provider domain must not be empty", nameof(domain));
        if (tokenLifetimeSeconds < 1)
            throw new ArgumentException($"Token lifetime must be positive, got {tokenLifetimeSeconds}", nameof(tokenLifetimeSeconds));
        _identity = identity;
        _domain = domain;
        _tokenLifetimeSeconds = tokenLifetimeSeconds;
    }

    public IdentityAssertion Issue(Fingerprint localFingerprint)
    {
        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var encodedKey = _identity.PublicKey.ToBase64Der();
        var token = JsonWebToken.Create(
            new JsonObject { [JsonWebToken.HeaderPublicKey] = encodedKey },
            new JsonObject
            {
                [JsonWebToken.ClaimExpiry] = issuedAt + _tokenLifetimeSeconds,
                [JsonWebToken.ClaimIssuedAt] = issuedAt,
                [JsonWebToken.ClaimPublicKey] = encodedKey,
            },
            _identity);
        return IdentityAssertion.Create(_domain, token,
            JsonWebSignature.SignDetached(localFingerprint.ToCanonicalPayload(), _identity));
    }
}

/// <summary>Validates the remote offer's <c>a=identity</c> assertion (port of <c>AssertionIdentityVerifier</c>).</summary>
public sealed class AssertionIdentityVerifier(bool allowAnonymous = false, ITokenVerifier? tokenVerifier = null)
{
    public bool AllowAnonymous { get; } = allowAnonymous;
    public ITokenVerifier? TokenVerifier { get; } = tokenVerifier;

    public PeerIdentity? Verify(string? attributeValue, Fingerprint remoteFingerprint)
    {
        if (attributeValue is null)
        {
            if (!AllowAnonymous)
                throw new IdentityException("Offer has no identity assertion");
            return null;
        }

        IdentityAssertion assertion;
        try
        {
            assertion = IdentityAssertion.Parse(attributeValue);
        }
        catch (IdentityException e)
        {
            throw new IdentityException("Identity assertion is malformed: " + e.Message, e);
        }

        var token = assertion.Token;
        token.CheckTimestamps(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        TokenVerifier?.Check(token);
        try
        {
            assertion.VerifyFingerprint(remoteFingerprint);
        }
        catch (IdentityException e)
        {
            throw new IdentityException("Identity assertion does not cover this connection: " + e.Message, e);
        }
        return new PeerIdentity(token.PublicKey, assertion.Domain, token);
    }
}
