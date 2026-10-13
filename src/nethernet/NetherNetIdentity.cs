using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zenith.NetherNet;

public sealed record NetherNetClientIdentity(string GameServerToken, bool TokenAuthenticated);

/// <summary>Creates the long-lived operator identity and the per-SDP identity assertions.</summary>
public sealed class NetherNetServerIdentity : IDisposable
{
    private readonly ECDsa _key;
    private readonly string _domain;

    private NetherNetServerIdentity(ECDsa key, string domain)
    {
        _key = key;
        _domain = domain;
    }

    public static NetherNetServerIdentity LoadOrCreate(string keyPath, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);

        var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        if (File.Exists(keyPath))
        {
            key.ImportPkcs8PrivateKey(File.ReadAllBytes(keyPath), out _);
        }
        else
        {
            File.WriteAllBytes(keyPath, key.ExportPkcs8PrivateKey());
        }
        return new NetherNetServerIdentity(key, domain);
    }

    public string AddAssertion(string answerSdp)
    {
        var fingerprints = NetherNetSdpIdentity.ReadFingerprints(answerSdp);
        var token = CreateServerToken();
        var signedFingerprints = SignFingerprints(fingerprints);
        var assertion = JsonSerializer.Serialize(new { token, fingerprints = signedFingerprints });
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            idp = new { domain = _domain, protocol = "default" },
            assertion
        });
        var identityLine = "a=identity:" + Convert.ToBase64String(envelope);
        return NetherNetSdpIdentity.InsertIdentity(answerSdp, identityLine);
    }

    private string CreateServerToken()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"ES384\",\"typ\":\"JWT\"}"));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            cpk = CreatePublicJwk(),
            iat = now,
            exp = now + 10L * 365 * 24 * 60 * 60
        });
        var body = header + "." + Base64Url(payload);
        var signature = _key.SignData(Encoding.ASCII.GetBytes(body), HashAlgorithmName.SHA384,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return body + "." + Base64Url(signature);
    }

    private string SignFingerprints(IReadOnlyList<NetherNetFingerprint> fingerprints)
    {
        var payload = NetherNetSdpIdentity.CanonicalFingerprintPayload(fingerprints);
        var encodedHeader = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"ES384\"}"));
        var signingInput = encodedHeader + "." + Base64Url(payload);
        var signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA384,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return encodedHeader + ".." + Base64Url(signature);
    }

    private object CreatePublicJwk()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        return new { kty = "EC", crv = "P-384", x = Base64Url(parameters.Q.X!), y = Base64Url(parameters.Q.Y!) };
    }

    public void Dispose() => _key.Dispose();

    internal static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class NetherNetSdpIdentity
{
    public static async Task<NetherNetClientIdentity?> ValidateOfferAsync(
        string offerSdp,
        Func<string, CancellationToken, Task<bool>>? validateGameServerToken,
        bool allowAnonymous,
        bool allowUnverifiedToken,
        CancellationToken cancellationToken = default)
    {
        var identity = ReadIdentityEnvelope(offerSdp);
        if (identity is null)
        {
            if (!allowAnonymous)
                throw new FormatException("NetherNet client identity is required by the configured authentication policy.");
            return null;
        }

        using var envelope = JsonDocument.Parse(DecodeBase64(identity));
        var root = envelope.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("idp", out var idp) || idp.ValueKind != JsonValueKind.Object ||
            !idp.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.String || protocol.GetString() != "default" ||
            !root.TryGetProperty("assertion", out var assertionValue) || assertionValue.ValueKind != JsonValueKind.String)
            throw new FormatException("Malformed NetherNet identity envelope.");

        using var assertion = JsonDocument.Parse(assertionValue.GetString() ?? string.Empty);
        if (assertion.RootElement.ValueKind != JsonValueKind.Object ||
            !assertion.RootElement.TryGetProperty("token", out var tokenValue) || tokenValue.ValueKind != JsonValueKind.String ||
            !assertion.RootElement.TryGetProperty("fingerprints", out var fingerprintsValue) || fingerprintsValue.ValueKind != JsonValueKind.String)
            throw new FormatException("NetherNet identity assertion is missing its token or fingerprint signature.");

        var token = tokenValue.GetString();
        var signedFingerprints = fingerprintsValue.GetString();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(signedFingerprints))
            throw new FormatException("NetherNet identity assertion contains an empty token or signature.");

        var authenticated = validateGameServerToken is not null &&
                           await validateGameServerToken(token, cancellationToken).ConfigureAwait(false);
        if (!authenticated && !allowUnverifiedToken)
            throw new CryptographicException("NetherNet GameServerToken signature or claims are invalid.");

        using var cpk = ReadCpk(token);
        VerifyFingerprints(offerSdp, signedFingerprints, cpk);

        return new NetherNetClientIdentity(token, authenticated);
    }

    public static string RemoveIdentity(string sdp)
    {
        var lines = SplitLines(sdp);
        return string.Join("\r\n", lines.Where(line => !line.StartsWith("a=identity:", StringComparison.OrdinalIgnoreCase))) + "\r\n";
    }

    public static string InsertIdentity(string sdp, string identityLine)
    {
        var lines = SplitLines(sdp);
        var mediaIndex = lines.FindIndex(line => line.StartsWith("m=", StringComparison.Ordinal));
        if (mediaIndex < 0)
            throw new FormatException("SDP has no media section for a NetherNet identity assertion.");
        if (lines.Any(line => line.StartsWith("a=identity:", StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("SDP already contains an identity assertion.");

        var lastFingerprint = -1;
        for (var i = 0; i < mediaIndex; i++)
        {
            if (lines[i].StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase))
                lastFingerprint = i;
        }
        lines.Insert(lastFingerprint >= 0 ? lastFingerprint + 1 : mediaIndex, identityLine);
        return string.Join("\r\n", lines) + "\r\n";
    }

    internal static IReadOnlyList<NetherNetFingerprint> ReadFingerprints(string sdp)
    {
        var fingerprints = new List<NetherNetFingerprint>();
        foreach (var line in SplitLines(sdp))
        {
            if (!line.StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line["a=fingerprint:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                throw new FormatException("Malformed SDP fingerprint.");
            fingerprints.Add(new NetherNetFingerprint(parts[0], parts[1]));
        }
        if (fingerprints.Count == 0)
            throw new FormatException("SDP has no DTLS fingerprints.");
        return fingerprints;
    }

    internal static byte[] CanonicalFingerprintPayload(IReadOnlyList<NetherNetFingerprint> fingerprints)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("fingerprint");
            writer.WriteStartArray();
            foreach (var fingerprint in fingerprints)
            {
                writer.WriteStartObject();
                writer.WriteString("algorithm", fingerprint.Algorithm);
                writer.WriteString("digest", fingerprint.Digest);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return output.ToArray();
    }

    private static string? ReadIdentityEnvelope(string sdp)
    {
        string? identity = null;
        foreach (var line in SplitLines(sdp))
        {
            if (line.StartsWith("m=", StringComparison.Ordinal))
                break;
            if (!line.StartsWith("a=identity:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (identity is not null)
                throw new FormatException("SDP contains multiple identity assertions.");
            identity = line["a=identity:".Length..];
        }
        return identity;
    }

    private static ECDsa ReadCpk(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new FormatException("Malformed GameServerToken JWT.");
        using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        if (payload.RootElement.ValueKind != JsonValueKind.Object ||
            !payload.RootElement.TryGetProperty("cpk", out var cpk))
            throw new FormatException("GameServerToken is missing a P-384 cpk public key.");

        // Client tokens may carry either a JWK or a Base64 DER SubjectPublicKeyInfo key.
        if (cpk.ValueKind == JsonValueKind.String)
        {
            var encoded = Convert.FromBase64String(cpk.GetString()!);
            var publicKey = ECDsa.Create();
            try
            {
                publicKey.ImportSubjectPublicKeyInfo(encoded, out var bytesRead);
                if (bytesRead != encoded.Length ||
                    publicKey.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP384.Oid.Value)
                    throw new FormatException("GameServerToken cpk must contain exactly one P-384 public key.");
                return publicKey;
            }
            catch
            {
                publicKey.Dispose();
                throw;
            }
        }

        if (cpk.ValueKind != JsonValueKind.Object ||
            !cpk.TryGetProperty("kty", out var kty) || kty.ValueKind != JsonValueKind.String || kty.GetString() != "EC" ||
            !cpk.TryGetProperty("crv", out var crv) || crv.ValueKind != JsonValueKind.String || crv.GetString() != "P-384" ||
            !cpk.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.String ||
            !cpk.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.String)
            throw new FormatException("GameServerToken is missing a P-384 cpk public key.");

        var xBytes = DecodeBase64Url(x.GetString() ?? string.Empty);
        var yBytes = DecodeBase64Url(y.GetString() ?? string.Empty);
        if (xBytes.Length != 48 || yBytes.Length != 48)
            throw new FormatException("GameServerToken cpk has invalid P-384 coordinates.");

        var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            Q = new ECPoint { X = xBytes, Y = yBytes }
        });
        return key;
    }

    private static void VerifyFingerprints(string sdp, string detachedJws, ECDsa cpk)
    {
        var parts = detachedJws.Split('.');
        if (parts.Length != 3 || parts[1].Length != 0)
            throw new FormatException("Malformed detached NetherNet fingerprint JWS.");
        using var header = JsonDocument.Parse(DecodeBase64Url(parts[0]));
        if (header.RootElement.ValueKind != JsonValueKind.Object ||
            !header.RootElement.TryGetProperty("alg", out var algorithm) || algorithm.ValueKind != JsonValueKind.String || algorithm.GetString() != "ES384")
            throw new CryptographicException("NetherNet fingerprint JWS must use ES384.");
        var fingerprints = ReadFingerprints(sdp);
        var input = Encoding.ASCII.GetBytes(parts[0] + "." + NetherNetServerIdentity.Base64Url(CanonicalFingerprintPayload(fingerprints)));
        if (!cpk.VerifyData(input, DecodeBase64Url(parts[2]), HashAlgorithmName.SHA384,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("NetherNet fingerprint signature does not match the SDP DTLS certificate.");
    }

    private static byte[] DecodeBase64(string text) => DecodeBase64Url(text.Replace('+', '-').Replace('/', '_'));

    private static byte[] DecodeBase64Url(string text)
    {
        var normalized = text.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(normalized);
    }

    private static List<string> SplitLines(string sdp) =>
        sdp.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.Length > 0).ToList();
}

internal readonly record struct NetherNetFingerprint(string Algorithm, string Digest);
