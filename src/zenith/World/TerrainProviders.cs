namespace Zenith.World;

static class TerrainProviders
{
    public const string ModeFlat = "flat";
    public const string ModeNoise = "noise";

    public static ITerrainProvider Create(
        string terrain,
        int seed,
        WorldGenerationDiagnostics? generationDiagnostics = null)
    {
        var mode = NormalizeMode(terrain);
        return mode switch
        {
            ModeFlat => FlatTerrainProvider.Instance,
            ModeNoise => new NoiseTerrainProvider(seed, generationDiagnostics),
            _ => throw new InvalidOperationException(
                $"world.terrain must be '{ModeFlat}' or '{ModeNoise}' (got '{terrain}').")
        };
    }

    public static string NormalizeMode(string? terrain)
        => (terrain ?? "").Trim().ToLowerInvariant();
}
