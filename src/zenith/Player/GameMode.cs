namespace Zenith.Player;

/// <summary>Join-time gamemode from config (ADR §31). No runtime switch until commands exist.</summary>
enum GameMode
{
    Survival = 0,
    Creative = 1
}

/// <summary>
/// Config string → <see cref="GameMode"/>. Lives beside the enum because C# enums cannot host methods
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
}
