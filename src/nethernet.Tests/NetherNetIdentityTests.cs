using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Zenith.NetherNet;

namespace Zenith.NetherNet.Tests;

public sealed class NetherNetIdentityTests
{
    private const string SampleSdp = "v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=fingerprint:sha-256 AA:BB:CC:DD\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_assertion_binds_authenticated_token_to_offer_fingerprint(bool cpkAsString)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var token = CreateToken(key, cpkAsString);
        var fingerprints = SignFingerprints(key, SampleSdp);
        var offer = AddClientAssertion(SampleSdp, token, fingerprints);

        var identity = await NetherNetSdpIdentity.ValidateOfferAsync(
            offer,
            (candidate, _) => Task.FromResult(candidate == token),
            allowAnonymous: false,
            allowUnverifiedToken: false);

        Assert.NotNull(identity);
        Assert.Equal(token, identity.GameServerToken);
        Assert.True(identity.TokenAuthenticated);
        Assert.DoesNotContain("a=identity:", NetherNetSdpIdentity.RemoveIdentity(offer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_assertion_rejects_fingerprint_tampering_and_untrusted_token(bool cpkAsString)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var token = CreateToken(key, cpkAsString);
        var fingerprints = SignFingerprints(key, SampleSdp);
        var alteredOffer = AddClientAssertion(SampleSdp.Replace("AA:BB", "FF:BB", StringComparison.Ordinal), token, fingerprints);

        await Assert.ThrowsAsync<CryptographicException>(() => NetherNetSdpIdentity.ValidateOfferAsync(
            alteredOffer, (_, _) => Task.FromResult(true), allowAnonymous: false, allowUnverifiedToken: false));

        var validOffer = AddClientAssertion(SampleSdp, token, fingerprints);
        await Assert.ThrowsAsync<CryptographicException>(() => NetherNetSdpIdentity.ValidateOfferAsync(
            validOffer, (_, _) => Task.FromResult(false), allowAnonymous: false, allowUnverifiedToken: false));
    }

    [Fact]
    public async Task Client_assertion_preserves_signed_fingerprint_text()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var token = CreateToken(key, cpkAsString: true);
        var sdp = SampleSdp.Replace("AA:BB:CC:DD", "aa:bb:cc:dd", StringComparison.Ordinal);
        var offer = AddClientAssertion(sdp, token, SignFingerprints(key, sdp));

        var identity = await NetherNetSdpIdentity.ValidateOfferAsync(
            offer, (candidate, _) => Task.FromResult(candidate == token),
            allowAnonymous: false, allowUnverifiedToken: false);

        Assert.NotNull(identity);
        Assert.True(identity.TokenAuthenticated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_assertion_rejects_signature_from_key_other_than_authenticated_token_cpk(bool cpkAsString)
    {
        using var tokenKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var token = CreateToken(tokenKey, cpkAsString);
        var offer = AddClientAssertion(SampleSdp, token, SignFingerprints(signingKey, SampleSdp));

        await Assert.ThrowsAsync<CryptographicException>(() => NetherNetSdpIdentity.ValidateOfferAsync(
            offer, (candidate, _) => Task.FromResult(candidate == token),
            allowAnonymous: false, allowUnverifiedToken: false));
    }

    [Theory]
    [InlineData("\"payload\"")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"cpk\":null}")]
    [InlineData("{\"cpk\":42}")]
    [InlineData("{\"cpk\":[]}")]
    [InlineData("{\"cpk\":\"not base64!\"}")]
    [InlineData("{\"cpk\":{\"kty\":42,\"crv\":\"P-384\",\"x\":\"\",\"y\":\"\"}}")]
    [InlineData("{\"cpk\":{\"kty\":\"EC\",\"crv\":42,\"x\":\"\",\"y\":\"\"}}")]
    [InlineData("{\"cpk\":{\"kty\":\"EC\",\"crv\":\"P-384\",\"x\":42,\"y\":\"\"}}")]
    [InlineData("{\"cpk\":{\"kty\":\"EC\",\"crv\":\"P-384\",\"x\":\"\",\"y\":null}}")]
    public async Task Client_assertion_rejects_malformed_cpk_payload(string payload)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var token = "e30." + Base64Url(Encoding.UTF8.GetBytes(payload)) + ".signature";
        var offer = AddClientAssertion(SampleSdp, token, SignFingerprints(key, SampleSdp));

        await Assert.ThrowsAsync<FormatException>(() => NetherNetSdpIdentity.ValidateOfferAsync(
            offer, (_, _) => Task.FromResult(true), allowAnonymous: false, allowUnverifiedToken: false));
    }

    [Theory]
    [InlineData("\"envelope\"")]
    [InlineData("{\"idp\":\"invalid\",\"assertion\":\"{}\"}")]
    [InlineData("{\"idp\":{\"protocol\":42},\"assertion\":\"{}\"}")]
    [InlineData("{\"idp\":{\"protocol\":\"default\"},\"assertion\":{}}")]
    [InlineData("{\"idp\":{\"protocol\":\"default\"},\"assertion\":\"[]\"}")]
    [InlineData("{\"idp\":{\"protocol\":\"default\"},\"assertion\":\"{\\\"token\\\":42,\\\"fingerprints\\\":\\\"signature\\\"}\"}")]
    [InlineData("{\"idp\":{\"protocol\":\"default\"},\"assertion\":\"{\\\"token\\\":\\\"token\\\",\\\"fingerprints\\\":42}\"}")]
    public async Task Client_assertion_rejects_malformed_envelope(string envelope)
    {
        var identityLine = "a=identity:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(envelope));
        var offer = NetherNetSdpIdentity.InsertIdentity(SampleSdp, identityLine);

        await Assert.ThrowsAsync<FormatException>(() => NetherNetSdpIdentity.ValidateOfferAsync(
            offer, (_, _) => Task.FromResult(true), allowAnonymous: false, allowUnverifiedToken: false));
    }

    [Theory]
    [InlineData("\"header\"")]
    [InlineData("{\"alg\":42}")]
    public async Task Client_assertion_rejects_malformed_fingerprint_signature_header(string header)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var token = CreateToken(key);
        var fingerprints = Base64Url(Encoding.UTF8.GetBytes(header)) + "..signature";
        var offer = AddClientAssertion(SampleSdp, token, fingerprints);

        await Assert.ThrowsAsync<CryptographicException>(() => NetherNetSdpIdentity.ValidateOfferAsync(
            offer, (candidate, _) => Task.FromResult(candidate == token),
            allowAnonymous: false, allowUnverifiedToken: false));
    }

    [Theory]
    [InlineData("P-256")]
    [InlineData("RSA")]
    [InlineData("trailing DER")]
    public async Task Client_assertion_rejects_cpk_that_is_not_one_complete_P384_public_key(string keyKind)
    {
        using var key = ECDsa.Create(keyKind == "P-256"
            ? ECCurve.NamedCurves.nistP256
            : ECCurve.NamedCurves.nistP384);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        if (keyKind == "RSA")
        {
            using var rsa = RSA.Create(2048);
            publicKey = rsa.ExportSubjectPublicKeyInfo();
        }
        if (keyKind == "trailing DER")
            Array.Resize(ref publicKey, publicKey.Length + 1);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { cpk = Convert.ToBase64String(publicKey) });
        var token = "e30." + Base64Url(payload) + ".signature";
        var offer = AddClientAssertion(SampleSdp, token, SignFingerprints(key, SampleSdp));

        var exception = await Record.ExceptionAsync(() => NetherNetSdpIdentity.ValidateOfferAsync(
            offer, (_, _) => Task.FromResult(true), allowAnonymous: false, allowUnverifiedToken: false));
        Assert.True(exception is FormatException or CryptographicException,
            $"Expected malformed or unsupported public key rejection, got {exception?.GetType().Name ?? "no exception"}.");
    }

    [Fact]
    public async Task Missing_identity_is_only_allowed_for_anonymous_policy()
    {
        var identity = await NetherNetSdpIdentity.ValidateOfferAsync(
            SampleSdp, null, allowAnonymous: true, allowUnverifiedToken: true);
        Assert.Null(identity);

        await Assert.ThrowsAsync<FormatException>(() => NetherNetSdpIdentity.ValidateOfferAsync(
            SampleSdp, null, allowAnonymous: false, allowUnverifiedToken: false));
    }

    [Fact]
    public void Server_assertion_is_signed_and_operator_key_survives_restart()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"zenith-nethernet-key-{Guid.NewGuid():N}.pk8");
        try
        {
            string firstToken;
            string firstAnswer;
            using (var identity = NetherNetServerIdentity.LoadOrCreate(keyPath, "server.example"))
            {
                firstAnswer = identity.AddAssertion(SampleSdp);
                firstToken = ReadAssertion(firstAnswer).Token;
            }

            using var restarted = NetherNetServerIdentity.LoadOrCreate(keyPath, "server.example");
            var restartedAnswer = restarted.AddAssertion(SampleSdp);
            var restartedToken = ReadAssertion(restartedAnswer).Token;
            Assert.Equal(ReadJwk(firstToken), ReadJwk(restartedToken));
            AssertSignedAssertion(firstAnswer);
            Assert.StartsWith("a=fingerprint:", firstAnswer.Split("\r\n")[4]);
            Assert.StartsWith("a=identity:", firstAnswer.Split("\r\n")[5]);
            Assert.StartsWith("m=", firstAnswer.Split("\r\n")[6]);
        }
        finally
        {
            if (File.Exists(keyPath)) File.Delete(keyPath);
        }
    }

    private static string AddClientAssertion(string sdp, string token, string fingerprints)
    {
        var assertion = JsonSerializer.Serialize(new { token, fingerprints });
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            idp = new { domain = "auth.minecraftservices.net", protocol = "default" },
            assertion
        });
        return NetherNetSdpIdentity.InsertIdentity(sdp, "a=identity:" + Convert.ToBase64String(envelope));
    }

    private static string CreateToken(ECDsa key, bool cpkAsString = false)
    {
        var parameters = key.ExportParameters(false);
        object cpk = cpkAsString
            ? Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
            : new
        {
            kty = "EC",
            crv = "P-384",
            x = Base64Url(parameters.Q.X!),
            y = Base64Url(parameters.Q.Y!)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { cpk });
        return "e30." + Base64Url(payload) + ".signature";
    }

    private static string SignFingerprints(ECDsa key, string sdp)
    {
        const string header = "eyJhbGciOiJFUzM4NCJ9"; // {"alg":"ES384"}
        var fingerprintLine = sdp.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("a=fingerprint:", StringComparison.Ordinal));
        var parts = fingerprintLine["a=fingerprint:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            fingerprint = new[] { new { algorithm = parts[0], digest = parts[1] } }
        });
        var encodedPayload = Base64Url(payload);
        var signingInput = Encoding.ASCII.GetBytes(header + "." + encodedPayload);
        var signature = key.SignData(signingInput, HashAlgorithmName.SHA384,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return header + ".." + Base64Url(signature);
    }

    private static void AssertSignedAssertion(string answerSdp)
    {
        var assertion = ReadAssertion(answerSdp);
        var key = ReadJwk(assertion.Token);
        var tokenParts = assertion.Token.Split('.');
        var tokenInput = Encoding.ASCII.GetBytes(tokenParts[0] + "." + tokenParts[1]);
        var tokenSignature = Decode(tokenParts[2]);
        using var cpk = CreatePublicKey(key);
        Assert.True(cpk.VerifyData(tokenInput, tokenSignature, HashAlgorithmName.SHA384,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        var fingerprintParts = assertion.Fingerprints.Split('.');
        Assert.Equal(3, fingerprintParts.Length);
        var fingerprintPayload = "{\"fingerprint\":[{\"algorithm\":\"sha-256\",\"digest\":\"AA:BB:CC:DD\"}]}";
        var fingerprintInput = Encoding.ASCII.GetBytes(fingerprintParts[0] + "." + Base64Url(Encoding.UTF8.GetBytes(fingerprintPayload)));
        Assert.True(cpk.VerifyData(fingerprintInput, Decode(fingerprintParts[2]), HashAlgorithmName.SHA384,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    private static (string Token, string Fingerprints) ReadAssertion(string sdp)
    {
        var line = sdp.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith("a=identity:", StringComparison.Ordinal));
        using var envelope = JsonDocument.Parse(Convert.FromBase64String(line["a=identity:".Length..]));
        using var assertion = JsonDocument.Parse(envelope.RootElement.GetProperty("assertion").GetString()!);
        return (assertion.RootElement.GetProperty("token").GetString()!,
            assertion.RootElement.GetProperty("fingerprints").GetString()!);
    }

    private static string ReadJwk(string token)
    {
        using var payload = JsonDocument.Parse(Decode(token.Split('.')[1]));
        return payload.RootElement.GetProperty("cpk").GetRawText();
    }

    private static ECDsa CreatePublicKey(string jwkText)
    {
        using var document = JsonDocument.Parse(jwkText);
        var jwk = document.RootElement;
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            Q = new ECPoint
            {
                X = Decode(jwk.GetProperty("x").GetString()!),
                Y = Decode(jwk.GetProperty("y").GetString()!)
            }
        });
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value += (value.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(value);
    }
}
