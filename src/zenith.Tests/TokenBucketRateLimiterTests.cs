using Xunit;
using Zenith.Session;

namespace Zenith.Tests;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public void Rejects_when_empty_and_refills_with_clock()
    {
        var clock = new FakeClock(DateTimeOffset.FromUnixTimeMilliseconds(1_000));
        var limiter = new TokenBucketRateLimiter<string>(capacity: 2, refillPerSecond: 10, clock);

        Assert.True(limiter.TryConsume("a"));
        Assert.True(limiter.TryConsume("a"));
        Assert.False(limiter.TryConsume("a"));

        clock.Advance(TimeSpan.FromMilliseconds(200)); // 2 tokens at 10/s
        Assert.True(limiter.TryConsume("a"));
        Assert.True(limiter.TryConsume("a"));
        Assert.False(limiter.TryConsume("a"));
    }

    [Fact]
    public void Keys_are_isolated_and_idle_buckets_are_cleaned()
    {
        var clock = new FakeClock(DateTimeOffset.FromUnixTimeMilliseconds(0));
        var limiter = new TokenBucketRateLimiter<int>(capacity: 1, refillPerSecond: 1, clock);

        Assert.True(limiter.TryConsume(1));
        Assert.False(limiter.TryConsume(1));
        Assert.True(limiter.TryConsume(2));

        clock.Advance(TimeSpan.FromSeconds(10));
        limiter.Cleanup(maxIdleMs: 5_000);
        Assert.True(limiter.TryConsume(1));
    }
}
