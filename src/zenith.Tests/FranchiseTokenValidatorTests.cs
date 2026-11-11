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
