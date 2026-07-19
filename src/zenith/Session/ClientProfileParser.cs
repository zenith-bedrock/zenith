using System.Text;
using System.Text.Json;

namespace Zenith.Session;

/// <summary>
/// Identity JWT / chain + ClientData → <see cref="ClientProfile"/> (ADR §59).
/// XUID string retained for PlayerList / chat (not only Guid derivation).
/// </summary>
static class ClientProfileParser
{
    public static ClientProfile Parse(
        string? identityToken,
        string? identityChainJson,
        string? clientDataJwt)
    {
        var xuid = TryReadXuid(identityToken, identityChainJson);
        var deviceId = "";
        var buildPlatform = -1;
        var platformChatId = "";

        if (!string.IsNullOrWhiteSpace(clientDataJwt))
        {
            try
            {
                using var payload = ReadPayload(clientDataJwt);
                var root = payload.RootElement;
                deviceId = ReadString(root, "DeviceId") ?? "";
                if (root.TryGetProperty("DeviceOS", out var os) && os.TryGetInt32(out var deviceOs))
                    buildPlatform = deviceOs;
                platformChatId = ReadString(root, "PlatformOnlineId") ?? "";
            }
            catch
            {
                // Soft: empty device fields — join still works.
            }
        }

        return new ClientProfile(xuid, deviceId, buildPlatform, platformChatId);
    }

    private static string TryReadXuid(string? identityToken, string? identityChainJson)
    {
        if (!string.IsNullOrWhiteSpace(identityToken))
        {
            try
            {
                using var doc = ReadPayload(identityToken);
                if (doc.RootElement.TryGetProperty("xid", out var xidEl))
                {
                    var xid = xidEl.GetString();
                    if (!string.IsNullOrWhiteSpace(xid))
                        return xid.Trim();
                }
            }
            catch
            {
                // fall through to chain
            }
        }

        if (!string.IsNullOrWhiteSpace(identityChainJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(identityChainJson);
                if (!doc.RootElement.TryGetProperty("chain", out var chain) ||
                    chain.ValueKind != JsonValueKind.Array)
                    return "";

                string? lastJwt = null;
                foreach (var el in chain.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                        lastJwt = el.GetString();
                }

                if (string.IsNullOrWhiteSpace(lastJwt))
                    return "";

                using var payload = ReadPayload(lastJwt);
                if (payload.RootElement.TryGetProperty("extraData", out var extra) &&
                    extra.TryGetProperty("XUID", out var xuidEl))
                {
                    var xuid = xuidEl.GetString();
                    if (!string.IsNullOrWhiteSpace(xuid))
                        return xuid.Trim();
                }
            }
            catch
            {
                return "";
            }
        }

        return "";
    }

    private static JsonDocument ReadPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            throw new FormatException("JWT missing payload.");
        var json = Encoding.UTF8.GetString(
            Convert.FromBase64String(PadBase64(parts[1].Replace('-', '+').Replace('_', '/'))));
        return JsonDocument.Parse(json);
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        return el.GetString();
    }

    private static string PadBase64(string input)
    {
        var pad = (4 - input.Length % 4) % 4;
        return pad == 0 ? input : input + new string('=', pad);
    }
}
