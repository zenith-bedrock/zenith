using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIV — golden-seed anchors (Part 49: detect accidental algorithm drift, not a full-world
/// snapshot) and biome distribution sanity (Part 11: catch pathological outcomes like 80% ocean or a
/// biome that effectively never appears, not chase an exact target split).
/// </summary>
public class WorldGenGoldenAndDistributionTests
{
    public WorldGenGoldenAndDistributionTests() => Blocks.EnsureLoaded();

    /// <summary>
    /// A handful of fixed seed+coordinate anchors. If any of these change, the height/climate
    /// algorithm shifted — intentional tuning should update the anchor deliberately, not silently.
    /// </summary>
    [Theory]
    [InlineData(12345, 0, 0, 64)]
    [InlineData(12345, 500, -500, 53)]
    [InlineData(7, 100, 100, 55)]
    public void Golden_seed_surface_anchors(int seed, int x, int z, int expectedSurfaceY)
    {
        Assert.Equal(expectedSurfaceY, OverworldTerrainSampler.SurfaceY(x, z, seed));
    }

    [Theory]
    [InlineData(12345, 0, 0, (int)OverworldBiomeKind.Plains)]
    [InlineData(12345, 500, -500, (int)OverworldBiomeKind.Plains)]
    [InlineData(7, 100, 100, (int)OverworldBiomeKind.Plains)]
    public void Golden_seed_biome_anchors(int seed, int x, int z, int expectedKind)
    {
        Assert.Equal((OverworldBiomeKind)expectedKind, OverworldBiomeSampler.SampleKind(x, z, seed));
    }

    /// <summary>
    /// Region-scale distribution across three fixed seeds. Not aiming for an equal split — just
    /// catching pathological outcomes (one biome dominating almost everything, or one never
    /// appearing at all across a wide sample).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(2024)]
    public void Biome_distribution_has_no_pathological_dominance(int seed)
    {
        var counts = new Dictionary<OverworldBiomeKind, int>();
        var total = 0;
        for (var x = -1024; x < 1024; x += 8)
        {
            for (var z = -1024; z < 1024; z += 8)
            {
                var kind = OverworldBiomeSampler.SampleKind(x, z, seed);
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                total++;
            }
        }

        Assert.True(total > 0);
        foreach (var kind in Enum.GetValues<OverworldBiomeKind>())
        {
            var fraction = counts.GetValueOrDefault(kind) / (double)total;
            Assert.True(fraction < 0.85, $"seed={seed} {kind} dominates at {fraction:P1} of the sampled region");
        }

        // Every kind must be reachable at this scale — a biome that never appears at all across a
        // 2048×2048 region (256×256 samples) is effectively dead code, not just rare.
        Assert.Equal(5, counts.Count);
    }
}
