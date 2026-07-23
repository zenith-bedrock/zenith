namespace Zenith.World;

/// <summary>Coarse overworld biomes for noise terrain (ADR §67 / §71 / §72). IDs match Bedrock network biome ids.</summary>
enum OverworldBiomeKind
{
    Ocean = 0,
    Plains = 1,
    Desert = 2,
    Hills = 3,
    Forest = 4,
}

/// <summary>
/// Climate FastNoiseLite → biome kind (PocketMine BiomeSelector-shaped lookup, mapped to Zenith's five kinds).
/// Surface block / trees stay discrete; <see cref="ContinuousHeightBias"/> stays smooth to avoid pillar cliffs.
/// </summary>
static class OverworldBiomeSampler
{
    /// <summary>Legacy cell size — kept for tests that scan biome neighborhoods; climate is continuous now.</summary>
    public const int BiomeCellSize = 48;

    public static OverworldBiomeKind SampleKind(int worldX, int worldZ, int seed)
    {
        SampleClimate(worldX, worldZ, seed, out var temperature, out var rainfall);
        return Lookup(temperature, rainfall);
    }

    public static void SampleClimate(int worldX, int worldZ, int seed, out double temperature, out double rainfall)
    {
        var fields = OverworldNoiseFields.For(seed);
        temperature = OverworldNoiseFields.Climate01(fields.Temperature.GetNoise(worldX, worldZ));
        rainfall = OverworldNoiseFields.Climate01(fields.Rainfall.GetNoise(worldX, worldZ));
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

    /// <summary>
    /// Smooth height offset from climate (no per-block hash). Weights overlap so biome flips do not cliff.
    /// Ocean weight is strong enough that full-ocean columns sit under sea without a hard Min clamp.
    /// </summary>
    public static double ContinuousHeightBias(double temperature, double rainfall)
    {
        // Soft region weights — wider edges than Lookup thresholds so surface leads the biome flip.
        var oceanW = 1.0 - Smoothstep(0.12, 0.40, rainfall);
        var hillsW = 1.0 - Smoothstep(0.12, 0.42, temperature);
        var desertW = Smoothstep(0.65, 0.85, temperature)
            * Smoothstep(0.25, 0.42, rainfall)
            * (1.0 - Smoothstep(0.52, 0.70, rainfall));

        // Ocean pull ≈ amplitude + margin so peak hills noise still goes under sea when oceanW≈1.
        return hillsW * 8.0 + desertW * (-2.0) + oceanW * (-(OverworldTerrainSampler.NoiseHillAmplitude + 6));
    }

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
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
