namespace Zenith.World;

static class TerrainProviders
{
    public const string ModeFlat = "flat";
    public const string ModeNoise = "noise";

    public static ITerrainProvider Create(string terrain, int seed)
    {
        var mode = NormalizeMode(terrain);
        return mode switch
        {
            ModeFlat => FlatTerrainProvider.Instance,
            ModeNoise => new NoiseTerrainProvider(seed),
            _ => throw new InvalidOperationException(
                $"world.terrain must be '{ModeFlat}' or '{ModeNoise}' (got '{terrain}').")
        };
    }

    public static string NormalizeMode(string? terrain)
        => (terrain ?? "").Trim().ToLowerInvariant();
}
