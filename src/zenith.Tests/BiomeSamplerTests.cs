using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class BiomeSamplerTests
{
    public BiomeSamplerTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Biome_sampler_is_deterministic()
    {
        const int seed = 42;
        var a = OverworldBiomeSampler.SampleKind(100, -50, seed);
        var b = OverworldBiomeSampler.SampleKind(100, -50, seed);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Desert_region_uses_sand_surface()
    {
        const int seed = 7;
        var (x, z) = FindBiomeCoords(seed, OverworldBiomeKind.Desert);
        var noise = new NoiseTerrainProvider(seed);
        var surface = OverworldTerrainSampler.SurfaceY(x, z, seed);
        Assert.Equal(Blocks.Sand, noise.SampleBaseBlock(x, surface, z));
    }

    [Fact]
    public void Ocean_surface_stays_below_sea_level()
    {
        const int seed = 12;
        var (x, z) = FindBiomeCoords(seed, OverworldBiomeKind.Ocean);
        var surface = OverworldTerrainSampler.SurfaceY(x, z, seed);
        Assert.True(surface < OverworldTerrainSampler.SeaLevel);
    }

    [Fact]
    public void Desert_has_no_trees_in_biome_cell()
    {
        const int seed = 21;
        var (x, z) = FindBiomeCoords(seed, OverworldBiomeKind.Desert);
        var noise = new NoiseTerrainProvider(seed);
        Assert.Equal(0, CountTreesInBiome(noise, seed, OverworldBiomeKind.Desert, x, z));
    }

    [Fact]
    public void Forest_has_trees_in_biome_cell()
    {
        const int seed = 21;
        var (x, z) = FindBiomeCoords(seed, OverworldBiomeKind.Forest);
        var noise = new NoiseTerrainProvider(seed);
        Assert.True(CountTreesInBiome(noise, seed, OverworldBiomeKind.Forest, x, z) > 0);
    }

    [Fact]
    public void Noise_columns_encode_different_biome_regions()
    {
        const int seed = 99;
        var noise = new NoiseTerrainProvider(seed);
        var desert = FindBiomeCoords(seed, OverworldBiomeKind.Desert);
        var forest = FindBiomeCoords(seed, OverworldBiomeKind.Forest);
        var desertCol = noise.GetBaseColumn(desert.X >> 4, desert.Z >> 4);
        var forestCol = noise.GetBaseColumn(forest.X >> 4, forest.Z >> 4);
        Assert.NotEqual(
            OverworldBiomeSampler.NetworkIdAt((desert.X >> 4) * 16 + 8, (desert.Z >> 4) * 16 + 8, seed),
            OverworldBiomeSampler.NetworkIdAt((forest.X >> 4) * 16 + 8, (forest.Z >> 4) * 16 + 8, seed));
        Assert.NotEqual(desertCol.Payload[^32..], forestCol.Payload[^32..]);
    }

    [Fact]
    public void Noise_spawn_biome_matches_sampler()
    {
        const int seed = 5;
        var noise = new NoiseTerrainProvider(seed);
        var expected = OverworldBiomeSampler.SampleSpawnBiome(0, 0, seed);
        Assert.Equal(expected, noise.SampleSpawnBiome(0, 0));
    }

    [Fact]
    public void Flat_spawn_biome_stays_plains()
    {
        Assert.Equal(SpawnBiome.Plains, FlatTerrainProvider.Instance.SampleSpawnBiome(0, 0));
    }

    private static (int X, int Z) FindBiomeCoords(int seed, OverworldBiomeKind kind)
    {
        for (var x = -256; x < 256; x += 4)
        {
            for (var z = -256; z < 256; z += 4)
            {
                if (OverworldBiomeSampler.SampleKind(x, z, seed) == kind)
                    return (x, z);
            }
        }

        throw new InvalidOperationException($"no {kind} biome found for seed {seed}");
    }

    private static int CountTreesInBiome(
        NoiseTerrainProvider noise,
        int seed,
        OverworldBiomeKind kind,
        int anchorX,
        int anchorZ)
    {
        var half = OverworldBiomeSampler.BiomeCellSize / 2;
        var count = 0;
        for (var x = anchorX - half; x < anchorX + half; x++)
        {
            for (var z = anchorZ - half; z < anchorZ + half; z++)
            {
                if (OverworldBiomeSampler.SampleKind(x, z, seed) != kind) continue;
                var surface = OverworldTerrainSampler.SurfaceY(x, z, seed);
                for (var y = surface + 1; y <= surface + 8; y++)
                {
                    if (noise.SampleBaseBlock(x, y, z) == Blocks.OakLog)
                        count++;
                }
            }
        }

        return count;
    }
}
