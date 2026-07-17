using System.Net;
using Xunit;
using Zenith.Raknet.Extension;

namespace Zenith.Raknet.Tests;

public class RateLimiterTests
{
    [Fact]
    public void TokenBucket_rejects_when_empty_and_refills_with_clock()
    {
        var now = 1_000L;
        var limiter = new TokenBucketRateLimiter<string>(capacity: 2, refillPerSecond: 10, () => now);

        Assert.True(limiter.TryConsume("a"));
        Assert.True(limiter.TryConsume("a"));
        Assert.False(limiter.TryConsume("a"));

        now += 200; // 2 tokens at 10/s
        Assert.True(limiter.TryConsume("a"));
        Assert.True(limiter.TryConsume("a"));
        Assert.False(limiter.TryConsume("a"));
    }

    [Fact]
    public void IpRateLimiter_isolates_addresses()
    {
        var now = 5_000L;
        var limiter = new IpRateLimiter(capacity: 1, refillPerSecond: 1, () => now);
        var a = IPAddress.Parse("10.0.0.1");
        var b = IPAddress.Parse("10.0.0.2");

        Assert.True(limiter.TryConsume(a));
        Assert.False(limiter.TryConsume(a));
        Assert.True(limiter.TryConsume(b));
        Assert.False(limiter.TryConsume(b));
    }
}
