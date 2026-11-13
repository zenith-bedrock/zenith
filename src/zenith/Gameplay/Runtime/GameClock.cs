using System.Diagnostics;

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
        _windowStartTimestamp = Stopwatch.GetTimestamp();
    }

    public void Advance()
    {
        CurrentTick++;
        WorldTime = (int)(CurrentTick % DayLengthTicks);

        _ticksInWindow++;
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = Stopwatch.GetElapsedTime(_windowStartTimestamp, now).TotalSeconds;
        if (elapsedSeconds >= 1.0)
        {
            MeasuredTps = _ticksInWindow / elapsedSeconds;
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
