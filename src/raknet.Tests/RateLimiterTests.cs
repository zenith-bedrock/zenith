using System.Net;
using Xunit;
using Zenith.Raknet.Extension;

namespace Zenith.Raknet.Tests;

public class RateLimiterTests
{
    [Fact]
    public void IpRateLimiter_isolates_addresses()
    {
        var limiter = new IpRateLimiter(capacity: 1, refillPerSecond: 1, new FakeClock(DateTimeOffset.FromUnixTimeMilliseconds(5_000)));
        var a = IPAddress.Parse("10.0.0.1");
        var b = IPAddress.Parse("10.0.0.2");

        Assert.True(limiter.TryConsume(a));
        Assert.False(limiter.TryConsume(a));
        Assert.True(limiter.TryConsume(b));
        Assert.False(limiter.TryConsume(b));
    }

    [Fact]
    public void IpRateLimiter_refills_with_the_injected_clock_and_drops_idle_buckets()
    {
        var clock = new FakeClock(DateTimeOffset.FromUnixTimeMilliseconds(5_000));
        var limiter = new IpRateLimiter(capacity: 1, refillPerSecond: 1, clock);
        var a = IPAddress.Parse("10.0.0.1");

        Assert.True(limiter.TryConsume(a));
        Assert.False(limiter.TryConsume(a));

        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(limiter.TryConsume(a));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(limiter.TryConsume(a));
    }
}
