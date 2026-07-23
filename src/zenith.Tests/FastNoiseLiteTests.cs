using Zenith.World.Noise;
using Xunit;

namespace Zenith.Tests;

public class FastNoiseLiteTests
{
    private static FastNoiseLite Create(int seed, float frequency = 1f / 96f, int octaves = 4)
    {
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFractalType(FastNoiseLite.FractalType.FBm);
        n.SetFractalOctaves(octaves);
        n.SetFractalGain(0.5f);
        n.SetFrequency(frequency);
        return n;
    }

    [Fact]
    public void GetNoise_is_deterministic_for_seed()
    {
        var a = Create(42);
        var b = Create(42);
        Assert.Equal(a.GetNoise(10.5f, -3.25f), b.GetNoise(10.5f, -3.25f));
    }

    [Fact]
    public void GetNoise_stays_near_unit_range()
    {
        var n = Create(7, frequency: 1f / 64f);
        for (var x = -40; x < 40; x++)
        for (var z = -40; z < 40; z++)
            Assert.InRange(n.GetNoise(x, z), -1.25f, 1.25f);
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        Assert.NotEqual(Create(1).GetNoise(12.3f, 45.6f), Create(2).GetNoise(12.3f, 45.6f));
    }
}
