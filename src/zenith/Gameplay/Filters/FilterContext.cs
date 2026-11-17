using Zenith.Rulebook;
using Zenith.World;

namespace Zenith.Gameplay.Filters;

/// <summary>
/// The subject a filter is asked about: a block cell of the world (where a mob would stand or stands), the time of day, and what the world
/// itself says (weather, difficulty). Costly facts (light, biome, the sky above) are read when a test asks for them, once per context.
/// </summary>
sealed class FilterContext(World.World world, int x, int y, int z, int worldTime)
{
    private int _weatherLight = -1;
    private int _plainLight = -1;
    private BiomeInfo? _biome;
    private int _sees = -1;

    public World.World World { get; } = world;
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Z { get; } = z;
    public int WorldTime { get; } = worldTime;

    public Difficulty Difficulty => World.Difficulty.Value;

    public BiomeInfo Biome => _biome ??= BiomeCatalog.Default.Of(World.SampleBiome(X, Z));

    /// <summary>Brightness of the cell, 0 to 15; with <paramref name="adjustForWeather"/> rain and thunder dim the sky part (the pack's <c>adjust_for_weather</c>).</summary>
    public int Light(bool adjustForWeather)
    {
        if (adjustForWeather) return _weatherLight >= 0 ? _weatherLight : _weatherLight = LightModel.Brightness(World, X, Y, Z, WorldTime, adjustForWeather: true);
        return _plainLight >= 0 ? _plainLight : _plainLight = LightModel.Brightness(World, X, Y, Z, WorldTime, adjustForWeather: false);
    }

    public bool SeesSky => (_sees >= 0 ? _sees : _sees = LightModel.SeesSky(World, X, Y, Z) ? 1 : 0) == 1;

    /// <summary>The block family name (<c>minecraft:grass_block</c>) at an offset from the cell, or null when it has none.</summary>
    public string? BlockNameAt(int dy) =>
        Blocks.Catalog.States.FamilyOf(World.GetBlock(X, Y + dy, Z))?.Name;
}
