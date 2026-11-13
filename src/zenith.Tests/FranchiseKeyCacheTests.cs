using System.Security.Cryptography;
using Xunit;
using Zenith.Session;

namespace Zenith.Tests;

public class FranchiseKeyCacheTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    [Fact]
    public async Task Keys_are_fetched_once_while_fresh_and_again_exactly_at_expiry()
    {
        var clock = new FakeClock();
        var fetches = 0;
        var cache = new FranchiseKeyCache(_ => Task.FromResult(NewKeys((byte)++fetches)), Lifetime, clock);

        Assert.Equal(1, Assert.Single(await cache.GetAsync(default)).Exponent![0]);
        clock.Advance(Lifetime - TimeSpan.FromTicks(1));
        Assert.Equal(1, Assert.Single(await cache.GetAsync(default)).Exponent![0]);
        Assert.Equal(1, fetches);

        clock.Advance(TimeSpan.FromTicks(1)); // expiry is exclusive: at the deadline the snapshot is no longer fresh
        Assert.Equal(2, Assert.Single(await cache.GetAsync(default)).Exponent![0]);
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_fetch()
    {
        var clock = new FakeClock();
        var fetches = 0;
        var release = new TaskCompletionSource();
        var cache = new FranchiseKeyCache(async _ =>
        {
            Interlocked.Increment(ref fetches);
            await release.Task;
            return NewKeys(1);
        }, Lifetime, clock);

        var callers = Enumerable.Range(0, 16).Select(_ => cache.GetAsync(default)).ToArray();
        release.SetResult();
        await Task.WhenAll(callers);

        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task A_failed_fetch_is_not_cached_and_the_next_caller_retries()
    {
        var clock = new FakeClock();
        var attempts = 0;
        var cache = new FranchiseKeyCache(_ => ++attempts == 1
            ? Task.FromException<IReadOnlyList<RSAParameters>>(new HttpRequestException("down"))
            : Task.FromResult(NewKeys(7)), Lifetime, clock);

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync(default));
        Assert.Equal(7, Assert.Single(await cache.GetAsync(default)).Exponent![0]);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Keys_are_never_served_past_their_expiry_when_the_refresh_fails()
    {
        var clock = new FakeClock();
        var fail = false;
        var cache = new FranchiseKeyCache(_ => fail
            ? Task.FromException<IReadOnlyList<RSAParameters>>(new HttpRequestException("down"))
            : Task.FromResult(NewKeys(1)), Lifetime, clock);

        await cache.GetAsync(default);
        clock.Advance(Lifetime);
        fail = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync(default));
    }

    [Fact]
    public async Task A_cancelled_waiter_does_not_poison_the_cache()
    {
        var clock = new FakeClock();
        var cache = new FranchiseKeyCache(_ => Task.FromResult(NewKeys(3)), Lifetime, clock);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(cancelled.Token));
        Assert.Equal(3, Assert.Single(await cache.GetAsync(default)).Exponent![0]);
    }

    private static IReadOnlyList<RSAParameters> NewKeys(byte marker) =>
        [new RSAParameters { Modulus = [1], Exponent = [marker] }];
}
