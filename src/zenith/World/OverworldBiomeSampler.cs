namespace Zenith.World;

/// <summary>Coarse overworld biomes for noise terrain (ADR §67 / §71). IDs match Bedrock network biome ids.</summary>
enum OverworldBiomeKind
{
    Ocean = 0,
    Plains = 1,
    Desert = 2,
    Hills = 3,
    Forest = 4,
}

/// <summary>
/// Climate Simplex → biome kind (PocketMine BiomeSelector-shaped lookup, mapped to Zenith's five kinds).
/// Surface block / trees / height bias stay here; height amplitude lives in <see cref="OverworldTerrainSampler"/>.
/// </summary>
static class OverworldBiomeSampler
{
    /// <summary>Legacy cell size — kept for tests that scan biome neighborhoods; climate is continuous now.</summary>
    public const int BiomeCellSize = 48;

    public static OverworldBiomeKind SampleKind(int worldX, int worldZ, int seed)
    {
        var fields = OverworldNoiseFields.For(seed);
        // PM: (noise2D normalized + 1) / 2 → [0,1]
        var temperature = (fields.Temperature.Noise2D(worldX, worldZ, normalized: true) + 1.0) * 0.5;
        var rainfall = (fields.Rainfall.Noise2D(worldX, worldZ, normalized: true) + 1.0) * 0.5;
        temperature = Math.Clamp(temperature, 0.0, 1.0);
        rainfall = Math.Clamp(rainfall, 0.0, 1.0);
        return Lookup(temperature, rainfall);
    }

    /// <summary>PM Normal lookup condensed onto Ocean/Plains/Desert/Hills/Forest.</summary>
    internal static OverworldBiomeKind Lookup(double temperature, double rainfall)
    {
        if (rainfall < 0.25)
            return temperature < 0.85 ? OverworldBiomeKind.Ocean : OverworldBiomeKind.Plains;
        if (rainfall < 0.60)
        {
            if (temperature < 0.25) return OverworldBiomeKind.Hills;
            if (temperature < 0.75) return OverworldBiomeKind.Plains;
            return OverworldBiomeKind.Desert;
        }

        if (rainfall < 0.80)
            return temperature < 0.25 ? OverworldBiomeKind.Hills : OverworldBiomeKind.Forest;

        return temperature < 0.40 ? OverworldBiomeKind.Hills : OverworldBiomeKind.Ocean;
    }

    public static int NetworkId(OverworldBiomeKind kind) => (int)kind;

    public static int NetworkIdAt(int worldX, int worldZ, int seed)
        => NetworkId(SampleKind(worldX, worldZ, seed));

    public static SpawnBiome SampleSpawnBiome(int worldX, int worldZ, int seed)
    {
        var kind = SampleKind(worldX, worldZ, seed);
        return new SpawnBiome((short)NetworkId(kind), WireName(kind));
    }

    public static string WireName(OverworldBiomeKind kind)
        => kind switch
        {
            OverworldBiomeKind.Ocean => "ocean",
            OverworldBiomeKind.Desert => "desert",
            OverworldBiomeKind.Hills => "extreme_hills",
            OverworldBiomeKind.Forest => "forest",
            _ => "plains",
        };

    public static int SurfaceHeightBias(OverworldBiomeKind kind, uint localHash)
        => kind switch
        {
            OverworldBiomeKind.Hills => 10 + (int)(localHash % 6),
            OverworldBiomeKind.Desert => -2,
            OverworldBiomeKind.Ocean => -(8 + (int)(localHash % 6)),
            _ => 0,
        };

    public static int SurfaceBlock(OverworldBiomeKind kind)
        => kind == OverworldBiomeKind.Desert ? Blocks.Sand : Blocks.GrassBlock;

    public static int SubsurfaceBlock(OverworldBiomeKind kind)
        => kind == OverworldBiomeKind.Desert ? Blocks.Sand : Blocks.Dirt;

    public static bool AllowsTrees(OverworldBiomeKind kind)
        => kind is OverworldBiomeKind.Plains or OverworldBiomeKind.Forest or OverworldBiomeKind.Hills;

    public static bool TryTreeRoll(uint cellHash, OverworldBiomeKind kind)
    {
        var mod = (int)(cellHash % 9);
        return kind switch
        {
            OverworldBiomeKind.Forest => mod > 3,
            OverworldBiomeKind.Plains => mod > 1,
            OverworldBiomeKind.Hills => mod > 2,
            _ => false,
        };
    }
}
