using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zenith.Session;

/// <summary>Validates Bedrock GameServerToken JWTs against the Franchise authorization key set.</summary>
internal static class FranchiseTokenValidator
{
    private const string KeySetUrl = "https://authorization.franchise.minecraft-services.net/.well-known/keys";
    private const string Issuer = "https://authorization.franchise.minecraft-services.net/";
    private const string Audience = "api://auth-minecraft-services/multiplayer";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static IReadOnlyList<RSAParameters>? _keys;
    private static DateTimeOffset _keysExpireAt;

    public static Task<bool> ValidateAsync(
        string token, CancellationToken cancellationToken, Action<string>? onFailure = null) =>
        ValidateAsync(token, GetKeysAsync, cancellationToken, onFailure);

    internal static async Task<bool> ValidateAsync(
        string token,
        Func<CancellationToken, Task<IReadOnlyList<RSAParameters>?>> getKeys,
        CancellationToken cancellationToken,
        Action<string>? onFailure = null)
    {
        bool Reject(string reason)
        {
            onFailure?.Invoke(reason);
            return false;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return Reject("Malformed GameServerToken JWT.");
            using var header = JsonDocument.Parse(Decode(parts[0]));
            using var payload = JsonDocument.Parse(Decode(parts[1]));
            var headerRoot = header.RootElement;
            var claims = payload.RootElement;
            if (headerRoot.ValueKind != JsonValueKind.Object || claims.ValueKind != JsonValueKind.Object)
                return Reject("JWT header and claims must be JSON objects.");
            if (!headerRoot.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String || alg.GetString() != "RS256")
                return Reject("GameServerToken must use RS256.");
            if (!claims.TryGetProperty("iss", out var issuer) || issuer.ValueKind != JsonValueKind.String || issuer.GetString() != Issuer)
                return Reject("GameServerToken issuer does not match the Franchise authorization service.");
            if (!HasAudience(claims))
                return Reject("GameServerToken audience does not include Minecraft multiplayer.");
            if (!claims.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number || !exp.TryGetInt64(out var expires) ||
                expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return Reject("GameServerToken is expired or has an invalid exp claim.");
            if (claims.TryGetProperty("nbf", out var nbf) &&
                (nbf.ValueKind != JsonValueKind.Number || !nbf.TryGetInt64(out var notBefore) ||
                 notBefore > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60))
                return Reject("GameServerToken is not yet valid or has an invalid nbf claim.");

            var keys = await getKeys(cancellationToken).ConfigureAwait(false);
            if (keys is null || keys.Count == 0)
                return Reject("Franchise signing keys are unavailable.");
            var data = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var signature = Decode(parts[2]);
            foreach (var parameters in keys)
            {
                using var key = RSA.Create(parameters);
                if (key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return true;
            }
            return Reject("GameServerToken signature does not match the Franchise signing keys.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or
                                           FormatException or CryptographicException or InvalidOperationException)
        {
            var failure = exception is HttpRequestException { StatusCode: { } status }
                ? $"HTTP {(int)status}"
                : exception.GetType().Name;
            return Reject($"GameServerToken validation failed: {failure}.");
        }
    }

    private static bool HasAudience(JsonElement claims)
    {
        if (!claims.TryGetProperty("aud", out var audience))
            return false;
        if (audience.ValueKind == JsonValueKind.String)
            return audience.GetString() == Audience;
        return audience.ValueKind == JsonValueKind.Array && audience.EnumerateArray()
            .Any(entry => entry.ValueKind == JsonValueKind.String && entry.GetString() == Audience);
    }

    private static async Task<IReadOnlyList<RSAParameters>?> GetKeysAsync(CancellationToken cancellationToken)
    {
        if (_keys is not null && _keysExpireAt > DateTimeOffset.UtcNow)
            return _keys;

        await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_keys is not null && _keysExpireAt > DateTimeOffset.UtcNow)
                return _keys;

            using var response = await Http.GetAsync(KeySetUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var keys = ReadKeys(body.RootElement);
            _keys = keys;
            _keysExpireAt = DateTimeOffset.UtcNow.AddHours(6);
            return keys;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    internal static IReadOnlyList<RSAParameters> ReadKeys(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("keys", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new FormatException("Franchise signing keys must be a JWKS keys array.");

        var keys = new List<RSAParameters>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("kty", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "RSA")
                continue;
            if (entry.TryGetProperty("use", out var use) &&
                (use.ValueKind != JsonValueKind.String || use.GetString() != "sig"))
                continue;
            if (entry.TryGetProperty("alg", out var alg) &&
                (alg.ValueKind != JsonValueKind.String || alg.GetString() != "RS256"))
                continue;
            if (!entry.TryGetProperty("n", out var modulus) || modulus.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("e", out var exponent) || exponent.ValueKind != JsonValueKind.String)
                throw new FormatException("Franchise RSA signing key is missing its modulus or exponent.");

            using var key = RSA.Create(new RSAParameters
            {
                Modulus = Decode(modulus.GetString()!),
                Exponent = Decode(exponent.GetString()!)
            });
            // Cache public parameters so a refresh cannot dispose a key another request is using.
            keys.Add(key.ExportParameters(false));
        }
        if (keys.Count == 0)
            throw new FormatException("Franchise JWKS contains no RS256 signing keys.");
        return keys;
    }

    private static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value += (value.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(value);
    }
}
