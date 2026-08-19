using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public class OverworldNoiseFieldsTests
{
    public OverworldNoiseFieldsTests() => OverworldNoiseFields.ClearForTests();

    [Fact]
    public void For_returns_the_same_cached_instance_for_the_same_seed()
    {
        var first = OverworldNoiseFields.For(42);
        var second = OverworldNoiseFields.For(42);

        Assert.Same(first, second);
    }

    [Fact]
    public void For_returns_distinct_instances_for_different_seeds()
    {
        var a = OverworldNoiseFields.For(1);
        var b = OverworldNoiseFields.For(2);

        Assert.NotSame(a, b);
    }

    /// <summary>Height/Temperature/Rainfall are seeded from distinct XOR constants specifically so
    /// they're uncorrelated — same seed must not produce identical noise output across the three fields.</summary>
    [Fact]
    public void Height_temperature_and_rainfall_fields_are_independently_seeded()
    {
        var fields = OverworldNoiseFields.For(7);

        var height = fields.Height.GetNoise(100f, 100f);
        var temperature = fields.Temperature.GetNoise(100f, 100f);
        var rainfall = fields.Rainfall.GetNoise(100f, 100f);

        Assert.False(height == temperature && temperature == rainfall);
    }

    [Fact]
    public void ClearForTests_drops_the_cache_so_a_later_call_creates_a_new_instance()
    {
        var first = OverworldNoiseFields.For(9);
        OverworldNoiseFields.ClearForTests();
        var second = OverworldNoiseFields.For(9);

        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData(-1f, 0.0)]
    [InlineData(0f, 0.5)]
    [InlineData(1f, 1.0)]
    public void Climate01_maps_the_noise_range_onto_0_to_1(float noise, double expected)
    {
        Assert.Equal(expected, OverworldNoiseFields.Climate01(noise), precision: 10);
    }

    [Theory]
    [InlineData(-5f)]
    [InlineData(5f)]
    public void Climate01_clamps_out_of_range_input(float noise)
    {
        var result = OverworldNoiseFields.Climate01(noise);
        Assert.InRange(result, 0.0, 1.0);
    }
}
