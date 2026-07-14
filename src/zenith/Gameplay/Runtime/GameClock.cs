namespace Zenith.Gameplay.Runtime;

/// <summary>
/// Relógio do gameplay: tick global, tempo do dia e TPS medido.
/// Sem I/O, Protocol ou PlayerManager.
/// </summary>
sealed class GameClock
{
    public const int TicksPerSecond = 20;
    public const int DayLengthTicks = 24000;

    public ulong CurrentTick { get; private set; }
    public int WorldTime { get; private set; }
    public double MeasuredTps { get; private set; } = TicksPerSecond;

    private long _windowStartTimestamp;
    private int _ticksInWindow;

    public GameClock()
    {
        _windowStartTimestamp = Environment.TickCount64;
    }

    public void Advance()
    {
        CurrentTick++;
        WorldTime = (int)(CurrentTick % DayLengthTicks);

        _ticksInWindow++;
        var now = Environment.TickCount64;
        var elapsedMs = now - _windowStartTimestamp;
        if (elapsedMs >= 1000)
        {
            MeasuredTps = _ticksInWindow * 1000.0 / elapsedMs;
            _ticksInWindow = 0;
            _windowStartTimestamp = now;
        }
    }

    /// <summary>Test helper: advance many ticks without sleeping.</summary>
    internal void AdvanceBy(int ticks)
    {
        for (var i = 0; i < ticks; i++)
            Advance();
    }
}
