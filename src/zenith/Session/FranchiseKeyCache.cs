using System.Security.Cryptography;

namespace Zenith.Session;

/// <summary>
/// Time-limited cache of the Franchise signing keys. The key list and its expiry are published together as one immutable snapshot,
/// so a reader can never see new keys with the old expiry (or the reverse). A failed fetch leaves the previous snapshot in place and
/// is retried by the next caller; nothing stale is served past its expiry.
/// </summary>
internal sealed class FranchiseKeyCache(
    Func<CancellationToken, Task<IReadOnlyList<RSAParameters>>> fetch,
    TimeSpan lifetime,
    TimeProvider? time = null)
{
    private sealed record Snapshot(IReadOnlyList<RSAParameters> Keys, DateTimeOffset ExpiresAt);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private volatile Snapshot? _current;

    public async Task<IReadOnlyList<RSAParameters>> GetAsync(CancellationToken cancellationToken)
    {
        if (Fresh() is { } hit)
            return hit;

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while this one waited for the gate.
            if (Fresh() is { } refreshed)
                return refreshed;

            var keys = await fetch(cancellationToken).ConfigureAwait(false);
            _current = new Snapshot(keys, _time.GetUtcNow() + lifetime);
            return keys;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private IReadOnlyList<RSAParameters>? Fresh() =>
        _current is { } snapshot && snapshot.ExpiresAt > _time.GetUtcNow() ? snapshot.Keys : null;
}
