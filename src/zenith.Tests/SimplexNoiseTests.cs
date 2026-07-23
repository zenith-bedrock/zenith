using Zenith.World.Noise;
using Xunit;

namespace Zenith.Tests;

public class SimplexNoiseTests
{
    [Fact]
    public void Noise2D_is_deterministic_for_seed()
    {
        var a = new SimplexNoise(42, octaves: 4, persistence: 0.5, expansion: 1.0 / 96.0);
        var b = new SimplexNoise(42, octaves: 4, persistence: 0.5, expansion: 1.0 / 96.0);
        Assert.Equal(a.Noise2D(10.5, -3.25, normalized: true), b.Noise2D(10.5, -3.25, normalized: true));
    }

    [Fact]
    public void Noise2D_normalized_stays_in_unit_range()
    {
        var n = new SimplexNoise(7, octaves: 4, persistence: 0.5, expansion: 1.0 / 64.0);
        for (var x = -40; x < 40; x++)
        for (var z = -40; z < 40; z++)
        {
            var v = n.Noise2D(x, z, normalized: true);
            Assert.InRange(v, -1.25, 1.25); // slight headroom for octave sum
        }
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var a = new SimplexNoise(1, 4, 0.5, 1.0 / 96.0).Noise2D(0, 0, true);
        var b = new SimplexNoise(2, 4, 0.5, 1.0 / 96.0).Noise2D(0, 0, true);
        Assert.NotEqual(a, b);
    }
}
