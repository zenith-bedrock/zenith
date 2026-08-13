namespace Zenith.Player;

/// <summary>
/// Phase XII consolidation: <see cref="Player"/> had grown ~15 hand-written Submit/TryConsume
/// pairs, all doing the same thing — a network/session thread hands data to the GameLoop, which
/// drains it at most once per tick — across three repeated shapes. These three types extract only
/// that storage + lock mechanism.
///
/// Named as mailboxes (a producer submits, a single consumer drains), not intents: "intent"
/// already means one specific thing in
/// Zenith (a bounded, gameplay-typed player action — attack, block edit, effect command, ...).
/// These types are the plumbing underneath an intent, not a replacement for the concept. They know
/// nothing about gameplay: no dispatch, no handlers, no routing, no typed event surface. Every
/// gameplay guard (e.g. "reject a new intent while dead") stays a plain <c>if</c> in
/// <see cref="Player"/> around the call — this is not an IntentBus/EventBus.
///
/// Synchronization: a single uncontended <see cref="object"/> lock per mailbox, same as every
/// hand-written version before it. Call volume is one submit per player input packet (tens/sec at
/// most per player) against one drain per GameLoop tick (20/sec) — nowhere near the point where
/// lock overhead would show up in the diagnostics tick budget. A lock-free SPSC ring only becomes
/// worth the added complexity if profiling ever shows contention here; nothing today suggests it
/// will, so this consolidation intentionally does not attempt that.
/// </summary>

/// <summary>Overwrite-latest boolean handoff — "this happened at least once since last tick".</summary>
sealed class PendingSignal
{
    private readonly object _lock = new();
    private bool _pending;

    public void Submit()
    {
        lock (_lock) _pending = true;
    }

    public bool TryConsume()
    {
        lock (_lock)
        {
            if (!_pending) return false;
            _pending = false;
            return true;
        }
    }
}

/// <summary>Overwrite-latest single-value handoff — a new submission replaces any unconsumed one.</summary>
sealed class PendingValue<T>
{
    private readonly object _lock = new();
    private T _pending = default!;
    private bool _hasPending;

    public void Submit(T value)
    {
        lock (_lock)
        {
            _pending = value;
            _hasPending = true;
        }
    }

    public bool TryConsume(out T value) => TryConsume(default!, out value);

    /// <summary>
    /// <paramref name="fallbackWhenEmpty"/> covers callers that want the current authoritative
    /// value (not <c>default</c>) when nothing is pending — e.g. GameMode's "no change" case.
    /// </summary>
    public bool TryConsume(T fallbackWhenEmpty, out T value)
    {
        lock (_lock)
        {
            if (!_hasPending)
            {
                value = fallbackWhenEmpty;
                return false;
            }

            value = _pending;
            _pending = default!;
            _hasPending = false;
            return true;
        }
    }

    /// <summary>
    /// Consumes only if the pending value satisfies <paramref name="predicate"/> — otherwise leaves
    /// it intact for a different owner to check. Added Phase XVIII for entity-interact routing: the
    /// target entity id is known at submit time, but which gameplay system owns that id is not known
    /// until several systems have each asked "is this mine?" in turn.
    /// </summary>
    public bool TryConsumeIf(Predicate<T> predicate, out T value)
    {
        lock (_lock)
        {
            if (!_hasPending || !predicate(_pending))
            {
                value = default!;
                return false;
            }

            value = _pending;
            _pending = default!;
            _hasPending = false;
            return true;
        }
    }
}

/// <summary>
/// Bounded producer-consumer FIFO handoff — refuses new submissions once full so a runaway
/// client cannot grow the queue without limit; the GameLoop drains one entry at a time.
/// </summary>
sealed class PendingMailbox<T>
{
    private readonly object _lock = new();
    private readonly Queue<T> _queue = new();
    private readonly int _capacity;

    public PendingMailbox(int capacity) => _capacity = capacity;

    public bool Submit(T value)
    {
        lock (_lock)
        {
            if (_queue.Count >= _capacity) return false;
            _queue.Enqueue(value);
            return true;
        }
    }

    public bool TryConsume(out T value)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                value = default!;
                return false;
            }

            value = _queue.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Reads the latest queued value without consuming it. This is only a handoff admission
    /// hint: the gameplay owner must still consume and validate the mailbox on its tick.
    /// </summary>
    public bool TryPeekLast(out T value)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                value = default!;
                return false;
            }

            value = default!;
            foreach (var entry in _queue)
                value = entry;
            return true;
        }
    }
}
