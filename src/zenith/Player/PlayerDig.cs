using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// One player's block-breaking state: the server-authoritative break in progress (written by the GameLoop) and the queue of dig
/// intents the network hands over (<see cref="DigIntent"/>). Both live behind one lock because the network decoder needs a coherent
/// snapshot (the break target together with the intents not yet applied) to authorise a same-packet Predict.
/// </summary>
sealed class PlayerDig(Func<bool> isDead)
{
    public const int MaxPendingDig = 4;

    /// <summary>
    /// Post-dig-window grace without dig AuthInput before aborting crack (~2s @ 20 TPS).
    /// Idle abort must not fire before <see cref="BreakRequiredTicks"/> elapse (§27).
    /// </summary>
    public const ulong DigIdleAbortTicks = 40;

    private readonly object _lock = new();
    private readonly Queue<DigIntent> _intents = new();
    private DigIntent? _provisional;
    private DigIntent? _suppressedAuthorization;

    // Server-authoritative break progress (AuthInput start -> predict). Cleared on abort/success.
    private int _targetX;
    private int _targetY;
    private int _targetZ;
    private ulong _startedTick;
    private int _requiredTicks;
    private StackId _heldStackId;
    private bool _hasTarget;
    private ulong _lastActivityTick;

    public int BreakTargetX { get { lock (_lock) return _targetX; } }
    public int BreakTargetY { get { lock (_lock) return _targetY; } }
    public int BreakTargetZ { get { lock (_lock) return _targetZ; } }
    public ulong BreakStartedTick { get { lock (_lock) return _startedTick; } }

    /// <summary>Dig duration snapshotted at <see cref="BeginBreak"/> (GameLoop ticks).</summary>
    public int BreakRequiredTicks { get { lock (_lock) return _requiredTicks; } }

    /// <summary>Held <see cref="StackId"/> at dig start / last retarget (ADR §55).</summary>
    public StackId DigHeldStackId { get { lock (_lock) return _heldStackId; } }

    public bool HasBreakTarget { get { lock (_lock) return _hasTarget; } }

    /// <summary>
    /// Last GameClock tick that saw start/crack/continue for the dig target.
    /// After <see cref="BreakRequiredTicks"/> + <see cref="DigIdleAbortTicks"/> without activity -> StopCrack.
    /// </summary>
    public ulong LastDigActivityTick { get { lock (_lock) return _lastActivityTick; } }

    /// <summary>GameLoop-only authoritative start; network submits <see cref="DigIntent"/> instead.</summary>
    public void BeginBreak(int x, int y, int z, ulong tick, int requiredTicks, StackId heldStackId = default)
    {
        lock (_lock)
        {
            _targetX = x;
            _targetY = y;
            _targetZ = z;
            _startedTick = tick;
            _requiredTicks = requiredTicks;
            _heldStackId = heldStackId;
            _hasTarget = true;
            _lastActivityTick = tick;
        }
    }

    /// <summary>Refresh dig activity (same-cell crack/continue) so idle abort does not fire.</summary>
    public void MarkDigActive(ulong tick)
    {
        lock (_lock)
        {
            if (!_hasTarget) return;
            _lastActivityTick = tick;
        }
    }

    /// <summary>Progress-preserving dig retarget when held tool changes mid-break (ADR §27).</summary>
    public void RetargetBreakTiming(ulong startedTick, int requiredTicks, StackId heldStackId)
    {
        lock (_lock)
        {
            if (!_hasTarget) return;
            _startedTick = startedTick;
            _requiredTicks = requiredTicks;
            _heldStackId = heldStackId;
        }
    }

    /// <summary>GameLoop-only authoritative abort; network submits an abort <see cref="DigIntent"/> instead.</summary>
    public void AbortBreak()
    {
        lock (_lock)
        {
            _hasTarget = false;
            _requiredTicks = 0;
            _heldStackId = default;
            _lastActivityTick = 0;
            _provisional = null;
        }
    }

    /// <summary>
    /// Network-side handoff only: suppresses authorization until the queued edit/abort is applied.
    /// It never mutates the tick-owned break target.
    /// </summary>
    public void CancelDigAuthorization(int x, int y, int z)
    {
        lock (_lock)
        {
            _suppressedAuthorization = DigIntent.Abort(x, y, z);
            _provisional = null;
            DropQueuedStart(x, y, z);
        }
    }

    public bool IsBreakTarget(int x, int y, int z)
    {
        lock (_lock)
            return _hasTarget && _targetX == x && _targetY == y && _targetZ == z;
    }

    /// <summary>Gets one coherent break-state snapshot for readers outside the GameLoop.</summary>
    public bool TryGetBreakState(out BreakState state)
    {
        lock (_lock)
        {
            if (!_hasTarget)
            {
                state = default;
                return false;
            }

            state = new BreakState(_targetX, _targetY, _targetZ, _startedTick, _requiredTicks, _heldStackId, _lastActivityTick);
            return true;
        }
    }

    /// <summary>Dig auth for same-packet Predict before tick applies <see cref="BeginBreak"/> (§27/§54).</summary>
    public bool TryGetDigAuth(int x, int y, int z, out ulong startedTick, out int requiredTicks)
    {
        lock (_lock)
        {
            if (_suppressedAuthorization is { } suppressed && suppressed.X == x && suppressed.Y == y && suppressed.Z == z)
            {
                startedTick = 0;
                requiredTicks = 0;
                return false;
            }

            if (_hasTarget && _targetX == x && _targetY == y && _targetZ == z)
            {
                startedTick = _startedTick;
                requiredTicks = _requiredTicks;
                return true;
            }

            if (_provisional is { HasValue: true, IsAbort: false } dig && dig.X == x && dig.Y == y && dig.Z == z)
            {
                startedTick = dig.StartedTick;
                requiredTicks = dig.RequiredTicks;
                return true;
            }
        }

        startedTick = 0;
        requiredTicks = 0;
        return false;
    }

    /// <summary>Queue dig start (handler). Provisional auth for same-packet Predict.</summary>
    public bool SubmitDigStart(int x, int y, int z, ulong startedTick, int requiredTicks, StackId heldStackId = default)
    {
        if (isDead()) return false;
        var intent = DigIntent.Start(x, y, z, startedTick, requiredTicks, heldStackId);
        lock (_lock)
        {
            if (_intents.Count >= MaxPendingDig)
                return false;
            _intents.Enqueue(intent);
            _provisional = intent;
            _suppressedAuthorization = null;
            return true;
        }
    }

    /// <summary>Queue dig abort (handler). Suppresses same-packet auth; tick owns the actual abort.</summary>
    public bool SubmitDigAbort(int x, int y, int z)
    {
        var intent = DigIntent.Abort(x, y, z);
        lock (_lock)
        {
            if (_intents.Count >= MaxPendingDig)
                return false;
            _intents.Enqueue(intent);
            _provisional = null;
            _suppressedAuthorization = intent;
            return true;
        }
    }

    /// <summary>Queues refresh activity; only BlockDigSystem updates the active dig timestamp.</summary>
    public bool SubmitDigActivity(int x, int y, int z, ulong tick)
    {
        lock (_lock)
        {
            if (_intents.Count >= MaxPendingDig) return false;
            _intents.Enqueue(DigIntent.Activity(x, y, z, tick));
            return true;
        }
    }

    public bool SubmitDigActivityForActive(ulong tick)
    {
        lock (_lock)
        {
            if (!_hasTarget || _intents.Count >= MaxPendingDig) return false;
            _intents.Enqueue(DigIntent.Activity(_targetX, _targetY, _targetZ, tick));
            return true;
        }
    }

    public bool TryConsumeDig(out DigIntent intent)
    {
        lock (_lock)
        {
            if (_intents.Count == 0)
            {
                intent = default;
                return false;
            }

            intent = _intents.Dequeue();
            return true;
        }
    }

    /// <summary>Drop a queued Start for the cell after DigAuthorized break is queued (same-packet Start+Predict).</summary>
    public void CancelPendingDigStart(int x, int y, int z)
    {
        lock (_lock)
        {
            _provisional = null;
            DropQueuedStart(x, y, z);
        }
    }

    /// <summary>Caller holds the lock.</summary>
    private void DropQueuedStart(int x, int y, int z)
    {
        if (_intents.Count == 0) return;
        var kept = new Queue<DigIntent>(_intents.Count);
        var removed = false;
        while (_intents.Count > 0)
        {
            var d = _intents.Dequeue();
            if (!removed && !d.IsAbort && d.X == x && d.Y == y && d.Z == z)
            {
                removed = true;
                continue;
            }

            kept.Enqueue(d);
        }

        while (kept.Count > 0)
            _intents.Enqueue(kept.Dequeue());
    }

    /// <summary>Coherent view of one active break target and its server-authoritative timing.</summary>
    public readonly record struct BreakState(
        int X,
        int Y,
        int Z,
        ulong StartedTick,
        int RequiredTicks,
        StackId HeldStackId,
        ulong LastActivityTick);
}
