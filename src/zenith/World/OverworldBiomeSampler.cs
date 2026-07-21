namespace Zenith.World;

/// <summary>Coarse overworld biomes for noise terrain (ADR §67). IDs match Bedrock network biome ids.</summary>
enum OverworldBiomeKind
{
    Ocean = 0,
    Plains = 1,
    Desert = 2,
    Hills = 3,
    Forest = 4,
}

/// <summary>
/// Deterministic 48×48 biome regions — surface block, height bias, trees, column wire id.
/// </summary>
static class OverworldBiomeSampler
{
    public const int BiomeCellSize = 48;

    private const int BiomeSalt = unchecked((int)0xB10EE001u);

    public static OverworldBiomeKind SampleKind(int worldX, int worldZ, int seed)
    {
        var cellX = FloorDiv(worldX, BiomeCellSize);
        var cellZ = FloorDiv(worldZ, BiomeCellSize);
        var h = Hash(cellX, cellZ, seed ^ BiomeSalt);
        var roll = (int)(h % 100);
        if (roll < 12) return OverworldBiomeKind.Ocean;
        if (roll < 24) return OverworldBiomeKind.Desert;
        if (roll < 44) return OverworldBiomeKind.Forest;
        if (roll < 58) return OverworldBiomeKind.Hills;
        return OverworldBiomeKind.Plains;
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

    private static int FloorDiv(int value, int divisor)
    {
        if (value >= 0) return value / divisor;
        return (value - (divisor - 1)) / divisor;
    }

    private static uint Hash(int x, int z, int seed)
    {
        unchecked
        {
            var h = (uint)seed;
            h ^= (uint)x * 374761393u;
            h ^= (uint)z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h;
        }
    }
}
