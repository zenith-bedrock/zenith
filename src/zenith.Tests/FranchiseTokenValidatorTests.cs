using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zenith.Session;
using Xunit;

namespace Zenith.Tests;

public sealed class FranchiseTokenValidatorTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("wrong-client", false)]
    [InlineData("wrong-curve", false)]
    [InlineData("tampered-client", false)]
    [InlineData("wrong-issuer-key", false)]
    [InlineData("expired", false)]
    [InlineData("missing-cpk", false)]
    [InlineData("missing-name", false)]
    [InlineData("missing-xuid", true)] // signed identity may use a UUID or remain ephemeral; cpk proof is still mandatory
    [InlineData("no-keys", false)]
    public void Full_login_requires_issuer_signature_and_client_proof(string scenario, bool accepted)
    {
        using var issuer = RSA.Create(2048);
        using var otherIssuer = RSA.Create(2048);
        using var client = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var otherClient = ECDsa.Create(scenario == "wrong-curve" ? ECCurve.NamedCurves.nistP256 : ECCurve.NamedCurves.nistP384);
        var claims = ValidClaims();
        claims["xname"] = "Synthetic Player"; claims["xid"] = "1234";
        claims["cpk"] = Convert.ToBase64String(client.ExportSubjectPublicKeyInfo());
        if (scenario == "wrong-curve") claims["cpk"] = Convert.ToBase64String(otherClient.ExportSubjectPublicKeyInfo());
        if (scenario == "expired") claims["exp"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        if (scenario == "missing-cpk") claims.Remove("cpk");
        if (scenario == "missing-name") claims.Remove("xname");
        if (scenario == "missing-xuid") claims.Remove("xid");
        var token = CreateToken(scenario == "wrong-issuer-key" ? otherIssuer : issuer, claims);
        var input = Base64Url("{\"alg\":\"ES384\"}"u8) + "." + Base64Url("{\"DeviceOS\":7}"u8);
        var signature = (scenario is "wrong-client" or "wrong-curve" ? otherClient : client).SignData(
            Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var clientData = input + "." + Base64Url(signature);
        if (scenario == "tampered-client")
            clientData = input.Split('.')[0] + "." + Base64Url("{\"DeviceOS\":8}"u8) + "." + Base64Url(signature);
        Assert.Equal(accepted, FranchiseTokenValidator.ValidateLogin(token, clientData,
            scenario == "no-keys" ? null : [issuer.ExportParameters(false)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_RSA_JWKS_authenticates_token_with_multiplayer_audience(bool audienceArray)
    {
        using var otherKey = RSA.Create(2048);
        using var signingKey = RSA.Create(2048);
        var keys = ReadPublishedKeys(otherKey, signingKey);
        var claims = ValidClaims();
        if (audienceArray)
            claims["aud"] = new[] { "other-service", "api://auth-minecraft-services/multiplayer" };
        var token = CreateToken(signingKey, claims);
        var failures = new List<string>();

        var authenticated = await FranchiseTokenValidator.ValidateAsync(
            token, _ => Task.FromResult<IReadOnlyList<RSAParameters>?>(keys),
            CancellationToken.None, failures.Add);

        Assert.True(authenticated);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("invalid exp")]
    [InlineData("future nbf")]
    [InlineData("invalid nbf")]
    [InlineData("algorithm")]
    public async Task Invalid_signed_claims_are_rejected_before_key_lookup(string invalidClaim)
    {
        using var key = RSA.Create(2048);
        var claims = ValidClaims();
        switch (invalidClaim)
        {
            case "issuer": claims["iss"] = "https://untrusted.example/"; break;
            case "audience": claims["aud"] = new[] { "other-service" }; break;
            case "expired": claims["exp"] = 0; break;
            case "invalid exp": claims["exp"] = "tomorrow"; break;
            case "future nbf": claims["nbf"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(); break;
            case "invalid nbf": claims["nbf"] = "yesterday"; break;
        }
        var token = CreateToken(key, claims, invalidClaim == "algorithm" ? "HS256" : "RS256");
        var keyLookups = 0;
        var failures = new List<string>();

        var authenticated = await FranchiseTokenValidator.ValidateAsync(
            token, _ =>
            {
                keyLookups++;
                return Task.FromResult<IReadOnlyList<RSAParameters>?>([key.ExportParameters(false)]);
            }, CancellationToken.None, failures.Add);

        Assert.False(authenticated);
        Assert.Equal(0, keyLookups);
        Assert.DoesNotContain(token, Assert.Single(failures));
    }

    [Fact]
    public async Task Signature_from_unpublished_key_is_rejected()
    {
        using var publishedKey = RSA.Create(2048);
        using var attackerKey = RSA.Create(2048);
        var keys = ReadPublishedKeys(publishedKey);
        var failures = new List<string>();

        var authenticated = await FranchiseTokenValidator.ValidateAsync(
            CreateToken(attackerKey, ValidClaims()),
            _ => Task.FromResult<IReadOnlyList<RSAParameters>?>(keys),
            CancellationToken.None, failures.Add);

        Assert.False(authenticated);
        Assert.Contains("signature", Assert.Single(failures));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_signing_keys_fail_closed_with_diagnostic(bool httpFailure)
    {
        using var key = RSA.Create(2048);
        var token = CreateToken(key, ValidClaims());
        var failures = new List<string>();

        var authenticated = await FranchiseTokenValidator.ValidateAsync(
            token, _ => httpFailure
                ? Task.FromException<IReadOnlyList<RSAParameters>?>(
                    new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable))
                : Task.FromResult<IReadOnlyList<RSAParameters>?>(null),
            CancellationToken.None, failures.Add);

        Assert.False(authenticated);
        var failure = Assert.Single(failures);
        Assert.Contains(httpFailure ? "HTTP 503" : "unavailable", failure);
        Assert.DoesNotContain(token, failure);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_without_invalid_token_diagnostic()
    {
        using var key = RSA.Create(2048);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failures = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FranchiseTokenValidator.ValidateAsync(
            CreateToken(key, ValidClaims()),
            token => Task.FromCanceled<IReadOnlyList<RSAParameters>?>(token),
            cancellation.Token, failures.Add));
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(1, true)]    // exp one second ahead of the clock
    [InlineData(0, false)]   // exp == now is already expired
    [InlineData(-1, false)]
    public void Expiry_is_judged_against_the_injected_clock_to_the_second(int expiresInSeconds, bool accepted)
    {
        using var key = RSA.Create(2048);
        var clock = new FakeClock();
        var claims = ValidClaims();
        claims["exp"] = clock.GetUtcNow().AddSeconds(expiresInSeconds).ToUnixTimeSeconds();

        var verdict = FranchiseTokenValidator.Validate(
            CreateToken(key, claims), [key.ExportParameters(false)], time: clock);

        Assert.Equal(accepted, verdict);
    }

    [Theory]
    [InlineData(60, true)]   // nbf may be up to 60 s ahead (clock skew allowance)
    [InlineData(61, false)]
    public void Not_before_allows_exactly_sixty_seconds_of_skew(int notBeforeInSeconds, bool accepted)
    {
        using var key = RSA.Create(2048);
        var clock = new FakeClock();
        var claims = ValidClaims();
        claims["exp"] = clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds();
        claims["nbf"] = clock.GetUtcNow().AddSeconds(notBeforeInSeconds).ToUnixTimeSeconds();

        Assert.Equal(accepted, FranchiseTokenValidator.Validate(
            CreateToken(key, claims), [key.ExportParameters(false)], time: clock));
    }

    [Fact]
    public void A_token_accepted_now_is_rejected_once_the_clock_passes_its_expiry()
    {
        using var key = RSA.Create(2048);
        var clock = new FakeClock();
        var claims = ValidClaims();
        claims["exp"] = clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds();
        var token = CreateToken(key, claims);
        var keys = new[] { key.ExportParameters(false) };

        Assert.True(FranchiseTokenValidator.Validate(token, keys, time: clock));
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(FranchiseTokenValidator.Validate(token, keys, time: clock));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"authenticationKeys\":[]}")]
    [InlineData("{\"keys\":[]}")]
    [InlineData("{\"keys\":[{\"kty\":\"RSA\",\"n\":42,\"e\":\"AQAB\"}]}")]
    [InlineData("{\"keys\":[{\"kty\":\"RSA\",\"n\":\"not base64!\",\"e\":\"AQAB\"}]}")]
    public void Invalid_JWKS_is_rejected(string json)
    {
        using var body = JsonDocument.Parse(json);
        Assert.Throws<FormatException>(() => FranchiseTokenValidator.ReadKeys(body.RootElement));
    }

    [Fact]
    public void JWKS_ignores_keys_with_other_types_purposes_or_algorithms()
    {
        using var key = RSA.Create(2048);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            keys = new object[]
            {
                new { kty = "EC", use = "sig" },
                new { kty = "RSA", use = "enc" },
                new { kty = "RSA", alg = "RS512" },
                PublicJwk(key)
            }
        }));

        var actual = Assert.Single(FranchiseTokenValidator.ReadKeys(body.RootElement));
        Assert.Equal(key.ExportParameters(false).Modulus, actual.Modulus);
    }

    private static IReadOnlyList<RSAParameters> ReadPublishedKeys(params RSA[] keys)
    {
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new { keys = keys.Select(PublicJwk) }));
        return FranchiseTokenValidator.ReadKeys(body.RootElement);
    }

    private static object PublicJwk(RSA key)
    {
        var parameters = key.ExportParameters(false);
        return new { kty = "RSA", use = "sig", n = Base64Url(parameters.Modulus!), e = Base64Url(parameters.Exponent!) };
    }

    private static Dictionary<string, object> ValidClaims() => new()
    {
        ["iss"] = "https://authorization.franchise.minecraft-services.net/",
        ["aud"] = "api://auth-minecraft-services/multiplayer",
        ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
    };

    private static string CreateToken(RSA key, object claims, string algorithm = "RS256")
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        var data = header + "." + payload;
        var signature = key.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return data + "." + Base64Url(signature);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
