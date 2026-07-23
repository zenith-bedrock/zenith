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
    public void Biome_lookup_covers_all_zenith_kinds()
    {
        var seen = new HashSet<OverworldBiomeKind>();
        for (var t = 0; t <= 20; t++)
        for (var r = 0; r <= 20; r++)
            seen.Add(OverworldBiomeSampler.Lookup(t / 20.0, r / 20.0));
        Assert.Contains(OverworldBiomeKind.Ocean, seen);
        Assert.Contains(OverworldBiomeKind.Plains, seen);
        Assert.Contains(OverworldBiomeKind.Desert, seen);
        Assert.Contains(OverworldBiomeKind.Hills, seen);
        Assert.Contains(OverworldBiomeKind.Forest, seen);
    }

    [Fact]
    public void Climate_biomes_vary_across_region()
    {
        const int seed = 99;
        var kinds = new HashSet<OverworldBiomeKind>();
        for (var x = -200; x < 200; x += 8)
        for (var z = -200; z < 200; z += 8)
            kinds.Add(OverworldBiomeSampler.SampleKind(x, z, seed));
        Assert.True(kinds.Count >= 3, $"expected ≥3 biome kinds, got {kinds.Count}");
    }

    [Fact]
    public void Continuous_height_bias_is_smooth_in_world_space()
    {
        const int seed = 99;
        var maxStep = 0.0;
        for (var x = -64; x < 64; x++)
        {
            for (var z = -64; z < 64; z++)
            {
                OverworldBiomeSampler.SampleClimate(x, z, seed, out var t0, out var r0);
                var b = OverworldBiomeSampler.ContinuousHeightBias(t0, r0);
                OverworldBiomeSampler.SampleClimate(x + 1, z, seed, out var t1, out var r1);
                maxStep = Math.Max(maxStep, Math.Abs(b - OverworldBiomeSampler.ContinuousHeightBias(t1, r1)));
                OverworldBiomeSampler.SampleClimate(x, z + 1, seed, out var t2, out var r2);
                maxStep = Math.Max(maxStep, Math.Abs(b - OverworldBiomeSampler.ContinuousHeightBias(t2, r2)));
            }
        }

        Assert.True(maxStep < 1.25, $"world-adjacent bias step {maxStep} too large");
    }

    private static (int X, int Z) FindBiomeCoords(int seed, OverworldBiomeKind kind)
    {
        for (var x = -512; x < 512; x += 2)
        {
            for (var z = -512; z < 512; z += 2)
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
