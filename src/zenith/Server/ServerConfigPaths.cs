namespace Zenith.Server;

/// <summary>
/// Resolves config path for boot. Default: next to the DLL (<see cref="AppContext.BaseDirectory"/>).
/// Containers may set <see cref="DataDirEnvironmentVariable"/> to a persistent data root (ADR §20 adendo).
/// </summary>
static class ServerConfigPaths
{
    /// <summary>Single optional env var — container data root only (not a general config matrix).</summary>
    public const string DataDirEnvironmentVariable = "ZENITH_DATA";

    public static string ResolveConfigPath()
    {
        var dataDir = ResolveDataDirectory();
        if (dataDir is not null)
            return Path.Combine(dataDir, ServerConfigLoader.DefaultFileName);
        return Path.Combine(AppContext.BaseDirectory, ServerConfigLoader.DefaultFileName);
    }

    public static string? ResolveDataDirectory()
    {
        var raw = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Path.GetFullPath(raw.Trim());
    }
}
