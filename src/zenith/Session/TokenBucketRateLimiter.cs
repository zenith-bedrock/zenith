using System.Collections.Concurrent;

namespace Zenith.Session;

/// <summary>
/// Token bucket genérico (mesmo espírito de <see cref="IpRateLimiter"/>), chaveável por qualquer T.
/// </summary>
internal sealed class TokenBucketRateLimiter<TKey> where TKey : notnull
{
    private sealed class Bucket
    {
        public double Tokens;
        public long LastRefillMs;
    }

    private readonly double _capacity;
    private readonly double _refillPerMs;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<TKey, Bucket> _buckets = new();

    public TokenBucketRateLimiter(double capacity, double refillPerSecond, TimeProvider? time = null)
    {
        _capacity = capacity;
        _refillPerMs = refillPerSecond / 1000.0;
        _time = time ?? TimeProvider.System;
    }

    public bool TryConsume(TKey key, double cost = 1)
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket { Tokens = _capacity, LastRefillMs = now });

        lock (bucket)
        {
            var elapsed = now - bucket.LastRefillMs;
            if (elapsed > 0)
            {
                bucket.Tokens = Math.Min(_capacity, bucket.Tokens + elapsed * _refillPerMs);
                bucket.LastRefillMs = now;
            }

            if (bucket.Tokens < cost) return false;
            bucket.Tokens -= cost;
            return true;
        }
    }

    public void Cleanup(long maxIdleMs)
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (var (key, bucket) in _buckets)
        {
            bool idle;
            lock (bucket) idle = now - bucket.LastRefillMs > maxIdleMs;
            if (idle) _buckets.TryRemove(key, out _);
        }
    }
}
