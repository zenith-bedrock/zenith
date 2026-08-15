using Xunit;
using Zenith.Gameplay.Entities;

namespace Zenith.Tests;

/// <summary>Phase XXIII — the shared yaw helper extracted after Zombie/Skeleton/Spider/Cow/Minecart duplicated the same atan2 formula.</summary>
public sealed class LookMathTests
{
    [Theory]
    [InlineData(0f, 1f, 0f)]      // +Z -> yaw 0
    [InlineData(1f, 0f, -90f)]    // +X -> yaw -90 (matches ProjectileSystemTests' existing "-90f => +X" fixture convention)
    [InlineData(-1f, 0f, 90f)]    // -X -> yaw 90
    [InlineData(0f, -1f, -180f)]  // -Z -> yaw ±180 (atan2's signed-zero branch picks -180 here; same angle as 180)
    public void YawTowards_matches_zeniths_established_coordinate_convention(float dx, float dz, float expectedYaw)
    {
        Assert.Equal(expectedYaw, LookMath.YawTowards(dx, dz), 3);
    }

    [Theory]
    [InlineData(200f, -160f)]
    [InlineData(-200f, 160f)]
    [InlineData(540f, 180f)]
    [InlineData(-540f, 180f)]
    [InlineData(45f, 45f)]
    public void NormalizeYaw_wraps_into_the_negative_180_to_180_range(float input, float expected)
    {
        Assert.Equal(expected, LookMath.NormalizeYaw(input), 3);
    }

    [Fact]
    public void MoveYawTowards_reaches_the_target_directly_when_within_the_turn_rate()
    {
        var result = LookMath.MoveYawTowards(current: 0f, desired: 5f, maxDeltaPerTick: 10f);
        Assert.Equal(5f, result, 3);
    }

    [Fact]
    public void MoveYawTowards_is_bounded_by_the_turn_rate_when_the_target_is_far()
    {
        var result = LookMath.MoveYawTowards(current: 0f, desired: 90f, maxDeltaPerTick: 10f);
        Assert.Equal(10f, result, 3);
    }

    [Fact]
    public void MoveYawTowards_turns_the_short_way_across_the_wraparound_seam()
    {
        // 179 -> -179 is a ~2 degree step across the seam, not a ~358 degree step the long way.
        var result = LookMath.MoveYawTowards(current: 179f, desired: -179f, maxDeltaPerTick: 10f);
        Assert.Equal(-179f, result, 3);
    }

    [Fact]
    public void MoveYawTowards_bounded_step_across_the_wraparound_seam_stays_short()
    {
        // From 179 toward -175 (a 6-degree short way across the seam) bounded to 2 degrees/tick
        // must land at -179 (179 + 2 wrapped), not swing 172 degrees the long way around.
        var result = LookMath.MoveYawTowards(current: 179f, desired: -175f, maxDeltaPerTick: 2f);
        Assert.Equal(-179f, result, 3);
    }

    [Fact]
    public void ShortestYawDelta_across_the_seam_is_small_not_the_long_way_around()
    {
        var delta = LookMath.ShortestYawDelta(from: 179f, to: -179f);
        Assert.Equal(2f, delta, 3);
    }
}
