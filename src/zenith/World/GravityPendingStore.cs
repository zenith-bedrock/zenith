using System.Threading;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Sparse pending gravity cells (ADR §57). SoftCap refuses new keys; re-enqueue of an
/// existing key is a no-op refresh. RAM-only — settled overlays are normal <c>ov:</c>.
/// </summary>
sealed class GravityPendingStore
{
    internal const int SoftCap = 2048;

    /// <summary>
    /// Max <em>new</em> falls started per GameLoop tick — not a cap on how many falls animate at
    /// once (that's <see cref="FallingBlockStore.SoftCap"/>). Kept low so a dug-out tower cascades
    /// gradually, one or two cells per tick, matching vanilla's staggered feel (ADR §57/§95).
    /// </summary>
    public const int MaxStepsPerTick = 2;

    private readonly HashSet<(int X, int Y, int Z)> _pending = new();
    private readonly Queue<(int X, int Y, int Z)> _queue = new();
    /// <summary>Cascade cells enqueued during a tick — promoted next tick (no same-tick chain).</summary>
    private readonly Queue<(int X, int Y, int Z)> _deferred = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    public GravityPendingStore(ILogger? logger = null) => _logger = logger;

    public int Count => _pending.Count;

    /// <summary>Move deferred cascade cells into the active queue (call once per tick).</summary>
    public void PromoteDeferred()
    {
        while (_deferred.Count > 0)
            _queue.Enqueue(_deferred.Dequeue());
    }

    /// <summary>Enqueue a cell for gravity evaluation. Existing keys OK at SoftCap.</summary>
    public bool TryEnqueue(int x, int y, int z) => TryEnqueueCore(x, y, z, immediate: true);

    /// <summary>Enqueue after a fall step — processed next tick so columns cascade gradually.</summary>
    public bool TryEnqueueDeferred(int x, int y, int z) => TryEnqueueCore(x, y, z, immediate: false);

    private bool TryEnqueueCore(int x, int y, int z, bool immediate)
    {
        var key = (x, y, z);
        if (_pending.Contains(key))
            return true;

        if (_pending.Count >= SoftCap)
        {
            if (Interlocked.Exchange(ref _capWarned, 1) == 0)
            {
                _logger?.Warning(
                    $"GravityPendingStore at SoftCap ({SoftCap}): refusing new pending fall cells.");
            }

            return false;
        }

        _pending.Add(key);
        if (immediate)
            _queue.Enqueue(key);
        else
            _deferred.Enqueue(key);
        return true;
    }

    /// <summary>Pop next pending cell, or false if empty.</summary>
    public bool TryDequeue(out int x, out int y, out int z)
    {
        while (_queue.Count > 0)
        {
            var key = _queue.Dequeue();
            if (!_pending.Remove(key))
                continue;
            x = key.X;
            y = key.Y;
            z = key.Z;
            return true;
        }

        x = y = z = 0;
        return false;
    }

    public void Clear()
    {
        _pending.Clear();
        _queue.Clear();
        _deferred.Clear();
    }
}
