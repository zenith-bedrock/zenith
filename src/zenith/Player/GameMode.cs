namespace Zenith.Player;

/// <summary>Join-time gamemode from config (ADR §31); runtime switch via §52.</summary>
enum GameMode
{
    Survival = 0,
    Creative = 1
}

/// <summary>
/// Config / chat arg → <see cref="GameMode"/>. Lives beside the enum because C# enums cannot host methods
/// (so <c>GameMode.FromConfig</c> is not possible without abandoning the enum).
/// </summary>
static class GameModeConfig
{
    public static GameMode FromConfig(string gamemode) =>
        gamemode switch
        {
            "Survival" => GameMode.Survival,
            "Creative" => GameMode.Creative,
            _ => throw new InvalidOperationException(
                $"Unexpected server.gamemode '{gamemode}' (config should have been validated).")
        };

    /// <summary>
    /// Parses <c>/gamemode</c> args: survival|creative|s|c|0|1 (case-insensitive).
    /// </summary>
    public static bool TryParseArg(string? arg, out GameMode mode)
    {
        mode = GameMode.Survival;
        if (string.IsNullOrWhiteSpace(arg)) return false;

        switch (arg.Trim().ToLowerInvariant())
        {
            case "survival":
            case "s":
            case "0":
                mode = GameMode.Survival;
                return true;
            case "creative":
            case "c":
            case "1":
                mode = GameMode.Creative;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Tries to parse a full chat line as <c>/gamemode …</c>.
    /// Returns false for non-matching slash lines (caller ignores) or bad args (<paramref name="badArgs"/>).
    /// </summary>
    public static bool TryParseCommand(string message, out GameMode mode, out bool badArgs)
    {
        mode = GameMode.Survival;
        badArgs = false;

        if (string.IsNullOrWhiteSpace(message)) return false;
        var trimmed = message.Trim();
        if (!trimmed.StartsWith('/')) return false;

        // "/gamemode" or "/gamemode <arg>" only — no aliases like /gm
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (!parts[0].Equals("/gamemode", StringComparison.OrdinalIgnoreCase))
            return false;

        if (parts.Length < 2 || !TryParseArg(parts[1], out mode))
        {
            badArgs = true;
            return false;
        }

        return true;
    }
}
