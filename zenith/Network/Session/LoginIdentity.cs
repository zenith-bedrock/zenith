using System.Text;
using System.Text.Json;

namespace Zenith.Network.Session;

/// <summary>
/// Extrai identidade do JWT de login Bedrock. Sem validação criptográfica ainda —
/// parsing puro, sem fluxo de sessão/state machine.
/// </summary>
static class LoginIdentity
{
    /// <summary>
    /// Decodifica o payload do JWT e retorna o claim <c>xname</c> (gamertag).
    /// </summary>
    public static string ExtractDisplayName(string jwtToken)
    {
        var parts = jwtToken.Split('.');
        if (parts.Length < 2) throw new FormatException("Invalid JWT token format.");

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payload = JsonDocument.Parse(payloadJson);

        if (!payload.RootElement.TryGetProperty("xname", out var displayName))
            throw new FormatException("Token does not contain a valid xname claim.");

        var value = displayName.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("The xname claim is empty.");

        return value;
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
}
