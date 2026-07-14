namespace Zenith.Player;

/// <summary>Join-time gamemode from config (ADR §31). No runtime switch until commands exist.</summary>
enum GameMode
{
    Survival = 0,
    Creative = 1
}

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
