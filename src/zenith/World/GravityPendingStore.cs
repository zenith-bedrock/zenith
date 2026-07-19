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

    /// <summary>Max fall steps processed per GameLoop tick (tower cascade bound).</summary>
    public const int MaxStepsPerTick = 64;

    private readonly HashSet<(int X, int Y, int Z)> _pending = new();
    private readonly Queue<(int X, int Y, int Z)> _queue = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    public GravityPendingStore(ILogger? logger = null) => _logger = logger;

    public int Count => _pending.Count;

    /// <summary>Enqueue a cell for gravity evaluation. Existing keys OK at SoftCap.</summary>
    public bool TryEnqueue(int x, int y, int z)
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
        _queue.Enqueue(key);
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
    }
}
