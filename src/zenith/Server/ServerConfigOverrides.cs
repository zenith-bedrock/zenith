namespace Zenith.Server;

/// <summary>
/// Panel-facing env overrides after <see cref="ServerConfigLoader.LoadOrCreate"/> (ADR §68).
/// Only documents conventions used by Pterodactyl / Wings — not a general env matrix.
/// </summary>
static class ServerConfigOverrides
{
    /// <summary>Pterodactyl default allocation port (overrides yaml when set).</summary>
    public const string ServerPortVariable = "SERVER_PORT";

    public static void ApplyFromEnvironment(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var portRaw = Environment.GetEnvironmentVariable(ServerPortVariable);
        if (!string.IsNullOrWhiteSpace(portRaw)
            && int.TryParse(portRaw.Trim(), out var port))
        {
            config.Server.Port = port;
        }
    }
}
