namespace Zenith.Gameplay.Runtime;

/// <summary>
/// The tick timetable, as pure arithmetic over <see cref="System.Diagnostics.Stopwatch"/> timestamps (no clock, no sleeping, so it is
/// exactly testable). Tick N is due at <c>start + N * interval</c>: deadlines are absolute, so a late wake-up or a long tick never
/// shifts the ones after it and the average rate stays exact however jittery any single wait was.
///
/// When the server falls behind it catches up by running the next ticks back to back, up to <c>maxLag</c>. Past that the lost time is
/// given up (the schedule restarts from "now") instead of running dozens of ticks in a burst, which would be a worse stall for every
/// client than the slow tick that caused it.
/// </summary>
struct TickSchedule
{
    private readonly long _interval;
    private readonly long _maxLag;
    private long _nextDeadline;

    public TickSchedule(long startTimestamp, long intervalTimestampTicks, long maxLagTimestampTicks)
    {
        if (intervalTimestampTicks <= 0) throw new ArgumentOutOfRangeException(nameof(intervalTimestampTicks));
        if (maxLagTimestampTicks < intervalTimestampTicks) throw new ArgumentOutOfRangeException(nameof(maxLagTimestampTicks), "The lag budget must allow at least one tick.");
        _interval = intervalTimestampTicks;
        _maxLag = maxLagTimestampTicks;
        _nextDeadline = startTimestamp;
    }

    /// <summary>When the next tick is due. A deadline already in the past means "run it now".</summary>
    public readonly long NextDeadline => _nextDeadline;

    /// <summary>How late the tick that starts at <paramref name="now"/> is, against its deadline (never negative).</summary>
    public readonly long Lateness(long now) => Math.Max(0, now - _nextDeadline);

    /// <summary>
    /// Records that the tick due at <see cref="NextDeadline"/> has finished at <paramref name="now"/> and schedules the following one.
    /// Returns how many whole ticks were given up because the server was further behind than the lag budget (0 in normal operation,
    /// including when it is merely catching up).
    /// </summary>
    public long Complete(long now)
    {
        _nextDeadline += _interval;
        var behind = now - _nextDeadline;
        if (behind <= _maxLag) return 0;

        var skipped = behind / _interval + 1;
        _nextDeadline = now + _interval;
        return skipped;
    }
}
