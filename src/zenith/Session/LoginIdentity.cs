using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zenith.Server;

namespace Zenith.Session;

/// <summary>
/// Identidade do login Bedrock: parsing puro + gate de verificação de chain.
/// Política vem de <see cref="ServerConfig.AuthSection"/> (auth.accept).
/// </summary>
static class LoginIdentity
{
    public readonly record struct ParsedIdentity(
        string DisplayName,
        Guid Uuid,
        /// <summary>True when UUID came from JWT <c>identity</c> / <c>leguuid</c> claim.</summary>
        bool IdentityFromJwt,
        /// <summary>False only for random Guid — inventory will not persist across rejoins.</summary>
        bool IdentityStable,
        byte[]? SkinRgba,
        uint SkinWidth,
        uint SkinHeight);

    public static string ExtractDisplayName(string jwtToken) =>
        ParseIdentityToken(jwtToken, auth: new ServerConfig.AuthSection { Accept = ["xbox", "self-signed", "offline"] })
            .DisplayName;

    public static ParsedIdentity ParseIdentityToken(
        string jwtToken,
        string? identityChainJson = null,
        string? clientDataJwt = null,
        ServerConfig.AuthSection? auth = null)
    {
        auth ??= LanDefaultAuth();
        auth.NormalizeAndValidate();

        JsonDocument? tokenDoc = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(jwtToken))
                tokenDoc = ReadPayload(jwtToken);

            var name = ResolveDisplayName(
                tokenDoc?.RootElement, identityChainJson, clientDataJwt, auth.AllowsOfflineFallback);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FormatException(auth.AllowsOfflineFallback
                    ? "No display name in token xname, chain extraData, or ClientData ThirdPartyName."
                    : "Token xname is missing or empty (auth.accept does not include offline).");
            }

            if (tokenDoc is not null)
            {
                var root = tokenDoc.RootElement;
                if (TryReadGuid(root, "identity", out var identityUuid))
                {
                    return new ParsedIdentity(name, identityUuid, IdentityFromJwt: true, IdentityStable: true,
                        SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
                }

                if (TryReadGuid(root, "leguuid", out var legacyUuid))
                {
                    return new ParsedIdentity(name, legacyUuid, IdentityFromJwt: true, IdentityStable: true,
                        SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
                }

                if (root.TryGetProperty("xid", out var xidEl))
                {
                    var xuid = xidEl.GetString();
                    if (!string.IsNullOrWhiteSpace(xuid))
                    {
                        return new ParsedIdentity(name, IdentityFromXuid(xuid), IdentityFromJwt: false, IdentityStable: true,
                            SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(identityChainJson) &&
                TryParseChainIdentity(identityChainJson, out var chainUuid))
            {
                return new ParsedIdentity(name, chainUuid, IdentityFromJwt: false, IdentityStable: true,
                    SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
            }

            if (auth.AllowsOfflineFallback)
            {
                if (!string.IsNullOrWhiteSpace(clientDataJwt) &&
                    TryReadClientDataGuid(clientDataJwt, "SelfSignedId", out var selfSignedId))
                {
                    return new ParsedIdentity(name, selfSignedId, IdentityFromJwt: false, IdentityStable: true,
                        SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
                }

                return new ParsedIdentity(name, IdentityFromOfflineName(name), IdentityFromJwt: false, IdentityStable: true,
                    SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
            }

            return new ParsedIdentity(name, Guid.NewGuid(), IdentityFromJwt: false, IdentityStable: false,
                SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
        }
        finally
        {
            tokenDoc?.Dispose();
        }
    }

    private static ServerConfig.AuthSection LanDefaultAuth()
    {
        var a = new ServerConfig.AuthSection();
        a.NormalizeAndValidate();
        return a;
    }

    /// <summary>MD5 v3 UUID from Xbox XUID — matches gophertunnel/PocketMine.</summary>
    internal static Guid IdentityFromXuid(string xuid)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("pocket-auth-1-xuid:" + xuid));
        return ToUuidV3(hash);
    }

    /// <summary>MD5 v3 UUID from offline display name — Java OfflinePlayer scheme for LAN soft auth.</summary>
    internal static Guid IdentityFromOfflineName(string displayName)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + displayName));
        return ToUuidV3(hash);
    }

    private static Guid ToUuidV3(byte[] hash)
    {
        Span<byte> id = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(id);
        id[6] = (byte)((id[6] & 0x0f) | 0x30);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return new Guid(id);
    }

    private static string? ResolveDisplayName(
        JsonElement? tokenRoot,
        string? identityChainJson,
        string? clientDataJwt,
        bool allowOfflineFallback)
    {
        if (tokenRoot is { } root &&
            root.TryGetProperty("xname", out var xname) &&
            !string.IsNullOrWhiteSpace(xname.GetString()))
            return xname.GetString()!.Trim();

        if (!allowOfflineFallback)
            return null;

        if (!string.IsNullOrWhiteSpace(identityChainJson) &&
            TryParseChainDisplayName(identityChainJson, out var chainName))
            return chainName;

        if (!string.IsNullOrWhiteSpace(clientDataJwt) &&
            TryReadClientDataString(clientDataJwt, "ThirdPartyName", out var thirdParty) &&
            !string.IsNullOrWhiteSpace(thirdParty))
            return thirdParty.Trim();

        return null;
    }

    private static bool TryParseChainDisplayName(string identityChainJson, out string displayName)
    {
        displayName = "";
        try
        {
            if (!TryGetChainLastPayload(identityChainJson, out var payload))
                return false;

            using (payload)
            {
                if (!payload.RootElement.TryGetProperty("extraData", out var extra)) return false;
                if (!extra.TryGetProperty("displayName", out var nameEl)) return false;
                var raw = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(raw)) return false;
                displayName = raw.Trim();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseChainIdentity(string identityChainJson, out Guid uuid)
    {
        uuid = Guid.Empty;
        try
        {
            if (!TryGetChainLastPayload(identityChainJson, out var payload))
                return false;

            using (payload)
            {
                if (!payload.RootElement.TryGetProperty("extraData", out var extra)) return false;
                if (!extra.TryGetProperty("identity", out var identityEl)) return false;

                var raw = identityEl.GetString();
                return !string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out uuid);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetChainLastPayload(string identityChainJson, out JsonDocument payload)
    {
        payload = null!;
        using var doc = JsonDocument.Parse(identityChainJson);
        if (!doc.RootElement.TryGetProperty("chain", out var chain) || chain.ValueKind != JsonValueKind.Array)
            return false;

        string? lastJwt = null;
        foreach (var el in chain.EnumerateArray())
        {
            var jwt = el.GetString();
            if (!string.IsNullOrWhiteSpace(jwt))
                lastJwt = jwt;
        }

        if (lastJwt is null) return false;
        payload = ReadPayload(lastJwt);
        return true;
    }

    private static bool TryReadGuid(JsonElement root, string property, out Guid uuid)
    {
        uuid = Guid.Empty;
        if (!root.TryGetProperty(property, out var el)) return false;
        var raw = el.GetString();
        return !string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out uuid);
    }

    private static bool TryReadClientDataGuid(string clientDataJwt, string property, out Guid uuid)
    {
        uuid = Guid.Empty;
        try
        {
            using var payload = ReadPayload(clientDataJwt);
            return TryReadGuid(payload.RootElement, property, out uuid);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadClientDataString(string clientDataJwt, string property, out string value)
    {
        value = "";
        try
        {
            using var payload = ReadPayload(clientDataJwt);
            if (!payload.RootElement.TryGetProperty(property, out var el)) return false;
            var raw = el.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            value = raw;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Tenta ler SkinData (base64 RGBA) do ClientData JWT.</summary>
    public static ParsedIdentity AttachClientSkin(ParsedIdentity identity, string clientDataJwt)
    {
        try
        {
            using var payload = ReadPayload(clientDataJwt);
            if (!payload.RootElement.TryGetProperty("SkinData", out var skinDataEl))
                return identity;

            var b64 = skinDataEl.GetString();
            if (string.IsNullOrWhiteSpace(b64)) return identity;

            var rgba = Convert.FromBase64String(b64.Replace('-', '+').Replace('_', '/').PadBase64());
            uint w = 64, h = 64;
            if (payload.RootElement.TryGetProperty("SkinImageWidth", out var ww) && ww.TryGetUInt32(out var width))
                w = width;
            if (payload.RootElement.TryGetProperty("SkinImageHeight", out var hh) && hh.TryGetUInt32(out var height))
                h = height;

            if (rgba.Length != w * h * 4) return identity;
            return identity with { SkinRgba = rgba, SkinWidth = w, SkinHeight = h };
        }
        catch
        {
            return identity;
        }
    }

    /// <summary>
    /// Valida estrutura da chain. Com <paramref name="requireStrictXbox"/>, exige
    /// verificação estrutural de assinaturas presentes.
    /// Sem a flag (LAN), só valida formato — NÃO é segurança.
    /// Offline modern clients may send a dummy empty JWT in the chain — skipped when soft.
    /// </summary>
    public static void ValidateIdentityChain(string identityChainJson, bool requireStrictXbox)
    {
        using var doc = JsonDocument.Parse(identityChainJson);
        if (!doc.RootElement.TryGetProperty("chain", out var chain) || chain.ValueKind != JsonValueKind.Array)
            throw new FormatException("Identity chain missing chain array.");

        if (chain.GetArrayLength() == 0)
            throw new FormatException("Identity chain is empty.");

        var sawJwt = false;
        foreach (var el in chain.EnumerateArray())
        {
            var jwt = el.GetString();
            if (string.IsNullOrWhiteSpace(jwt))
            {
                if (requireStrictXbox)
                    throw new FormatException("Empty JWT in identity chain.");
                continue;
            }

            _ = ReadPayload(jwt);
            sawJwt = true;
        }

        if (!sawJwt && requireStrictXbox)
            throw new FormatException("Identity chain has no verifiable JWT.");

        if (!requireStrictXbox) return;

        ValidateChainSignatures(chain);
    }

    private static void ValidateChainSignatures(JsonElement chain)
    {
        foreach (var el in chain.EnumerateArray())
        {
            var jwt = el.GetString();
            if (string.IsNullOrWhiteSpace(jwt)) continue;

            var parts = jwt.Split('.');
            if (parts.Length != 3) throw new FormatException("Malformed JWT in chain.");

            var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            using var header = JsonDocument.Parse(headerJson);
            if (!header.RootElement.TryGetProperty("x5u", out _))
                throw new FormatException("Chain JWT missing x5u; cannot verify.");

            var sig = Base64UrlDecode(parts[2]);
            if (sig.Length == 0) throw new FormatException("Empty JWT signature.");
        }
    }

    private static JsonDocument ReadPayload(string jwtToken)
    {
        var parts = jwtToken.Split('.');
        if (parts.Length < 2) throw new FormatException("Invalid JWT token format.");
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        return JsonDocument.Parse(payloadJson);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    private static string PadBase64(this string input)
    {
        switch (input.Length % 4)
        {
            case 2: return input + "==";
            case 3: return input + "=";
            default: return input;
        }
    }
}
