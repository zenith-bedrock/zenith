using System.Text;
using System.Text.Json;

namespace Zenith.Session;

/// <summary>
/// Identidade do login Bedrock: parsing puro + gate de verificação de chain.
/// <see cref="RequireChainSignatures"/> é definido no boot a partir de <c>zenith.yml</c>.
/// </summary>
static class LoginIdentity
{
    public readonly record struct ParsedIdentity(
        string DisplayName,
        Guid Uuid,
        bool IdentityFromJwt,
        byte[]? SkinRgba,
        uint SkinWidth,
        uint SkinHeight);

    /// <summary>Quando true, login rejeita chain sem assinaturas verificáveis. Setado no boot.</summary>
    public static bool RequireChainSignatures { get; set; }

    public static string ExtractDisplayName(string jwtToken) => ParseIdentityToken(jwtToken).DisplayName;

    public static ParsedIdentity ParseIdentityToken(string jwtToken)
    {
        var payload = ReadPayload(jwtToken);

        if (!payload.RootElement.TryGetProperty("xname", out var displayName))
            throw new FormatException("Token does not contain a valid xname claim.");

        var name = displayName.GetString();
        if (string.IsNullOrWhiteSpace(name))
            throw new FormatException("The xname claim is empty.");

        var uuid = Guid.NewGuid();
        var identityFromJwt = false;
        if (payload.RootElement.TryGetProperty("identity", out var identityClaim))
        {
            var raw = identityClaim.GetString();
            if (!string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out var parsed))
            {
                uuid = parsed;
                identityFromJwt = true;
            }
        }

        return new ParsedIdentity(name, uuid, identityFromJwt, SkinRgba: null, SkinWidth: 0, SkinHeight: 0);
    }

    /// <summary>Tenta ler SkinData (base64 RGBA) do ClientData JWT.</summary>
    public static ParsedIdentity AttachClientSkin(ParsedIdentity identity, string clientDataJwt)
    {
        try
        {
            var payload = ReadPayload(clientDataJwt);
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
    /// Valida estrutura da chain. Com <see cref="RequireChainSignatures"/>, exige
    /// verificação estrutural de assinaturas presentes.
    /// Sem a flag (LAN/dev), só valida formato — NÃO é segurança.
    /// </summary>
    public static void ValidateIdentityChain(string identityChainJson)
    {
        using var doc = JsonDocument.Parse(identityChainJson);
        if (!doc.RootElement.TryGetProperty("chain", out var chain) || chain.ValueKind != JsonValueKind.Array)
            throw new FormatException("Identity chain missing chain array.");

        if (chain.GetArrayLength() == 0)
            throw new FormatException("Identity chain is empty.");

        foreach (var el in chain.EnumerateArray())
        {
            var jwt = el.GetString() ?? throw new FormatException("Null chain entry.");
            _ = ReadPayload(jwt);
        }

        if (!RequireChainSignatures) return;

        ValidateChainSignatures(chain);
    }

    private static void ValidateChainSignatures(JsonElement chain)
    {
        foreach (var el in chain.EnumerateArray())
        {
            var jwt = el.GetString()!;
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
