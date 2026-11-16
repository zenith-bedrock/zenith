using System.Threading;
using Zenith.Logging;

namespace Zenith.World;

/// <summary>
/// Sparse pending gravity cells (ADR §57). SoftCap refuses new keys; re-enqueue of an existing key is a no-op (the earlier due tick
/// stands). RAM-only — settled overlays are normal <c>ov:</c>.
/// <para/>
/// A cell does not start to fall when it is enqueued but when its due tick comes (plan 003): vanilla schedules the block's tick
/// <see cref="StartDelayTicks"/> ahead, so an unsupported block hangs for that long, and a stack peels one block per delay because
/// each block is scheduled by the one below leaving.
/// </summary>
sealed class GravityPendingStore
{
    internal const int SoftCap = 2048;

    /// <summary>Ticks between a block losing its support and starting to fall (vanilla scheduled-tick delay of a falling block).</summary>
    public const int StartDelayTicks = 2;

    /// <summary>
    /// Max <em>new</em> falls started per GameLoop tick — a safety bound for very large collapses, not what paces an ordinary stack
    /// (the start delay does that). Not a cap on how many falls animate at once (<see cref="FallingBlockStore.SoftCap"/>).
    /// </summary>
    public const int MaxStepsPerTick = 64;

    private readonly HashSet<(int X, int Y, int Z)> _pending = new();
    private readonly Queue<(int X, int Y, int Z)> _queue = new();
    private readonly PriorityQueue<(int X, int Y, int Z), ulong> _scheduled = new();
    /// <summary>Cells noticed by a world write, which has no clock: gravity stamps them with a due tick on its next run.</summary>
    private readonly Queue<(int X, int Y, int Z)> _unstamped = new();
    private readonly ILogger? _logger;
    private int _capWarned;

    public GravityPendingStore(ILogger? logger = null) => _logger = logger;

    public int Count => _pending.Count;

    /// <summary>Moves every cell whose due tick has come into the ready queue (call once per tick).</summary>
    public void PromoteDue(ulong now)
    {
        while (_scheduled.TryPeek(out var key, out var due) && due <= now)
        {
            _scheduled.Dequeue();
            _queue.Enqueue(key);
        }
    }

    /// <summary>Gives every cell noticed since the last run its due tick, <paramref name="delayTicks"/> after <paramref name="now"/> (call before <see cref="PromoteDue"/>).</summary>
    public void StampNoticed(ulong now, int delayTicks)
    {
        while (_unstamped.TryDequeue(out var key))
            _scheduled.Enqueue(key, now + (ulong)delayTicks);
    }

    /// <summary>
    /// A world write noticed that this cell may have lost its support (or is a gravity block that was just placed). It has no clock, so
    /// the cell waits unstamped; the same cell noticed twice is one entry.
    /// </summary>
    public bool TryNotice(int x, int y, int z)
    {
        var key = (x, y, z);
        if (_pending.Contains(key)) return true;
        if (_pending.Count >= SoftCap) return TryEnqueue(x, y, z); // takes the shared refuse-and-warn path
        _pending.Add(key);
        _unstamped.Enqueue(key);
        return true;
    }

    /// <summary>Makes every scheduled cell ready now, whatever its due tick (shutdown settles everything).</summary>
    public void PromoteAll()
    {
        while (_unstamped.TryDequeue(out var noticed))
            _queue.Enqueue(noticed);
        while (_scheduled.TryDequeue(out var key, out _))
            _queue.Enqueue(key);
    }

    /// <summary>Enqueues a cell for gravity evaluation once <paramref name="dueTick"/> has come (0: at the next promotion).</summary>
    public bool TryEnqueue(int x, int y, int z, ulong dueTick = 0)
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
        _scheduled.Enqueue(key, dueTick);
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
        _scheduled.Clear();
        _unstamped.Clear();
    }
}
