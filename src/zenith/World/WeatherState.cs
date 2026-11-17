namespace Zenith.World;

/// <summary>
/// The weather of the world (plan 004 item 26): rain and thunder, each with its own timer as in vanilla. Thunder only shows while it
/// rains. Whatever changes it (the cycle, <c>/weather</c>) only touches this; <see cref="Gameplay.Replication.WeatherSystem"/> notices the
/// new <see cref="Version"/>, tells every client and stores it.
/// </summary>
sealed class WeatherState
{
    public const int DefaultCommandTicks = 6000;

    private bool _raining;
    private bool _thundering;
    private long _version;

    /// <summary>Ticks until rain starts (when clear) or stops (when raining).</summary>
    public int RainTicks { get; private set; } = 12000;

    /// <summary>Ticks until thunder starts or stops; it only shows during rain.</summary>
    public int ThunderTicks { get; private set; } = 12000;

    public bool Raining => _raining;

    /// <summary>Whether the thunder half of the cycle is on, even while it is not raining (saved so the cycle resumes in step).</summary>
    public bool ThunderCycleOn => _thundering;

    /// <summary>Thunder is in the sky and it is raining, so the storm shows.</summary>
    public bool Thunder => _raining && _thundering;

    /// <summary>0 clear, 1 rain, 2 thunderstorm.</summary>
    public int Level => !_raining ? 0 : _thundering ? 2 : 1;

    public long Version => Volatile.Read(ref _version);

    /// <summary>Starts a world with timers drawn the way a finished spell would draw them.</summary>
    public void Seed(Random random)
    {
        RainTicks = random.Next(12000, 180001);
        ThunderTicks = random.Next(12000, 180001);
    }

    /// <summary>One tick of the natural cycle: a timer that runs out flips its half of the weather and draws the next spell (vanilla durations).</summary>
    public void Advance(Random random)
    {
        if (--RainTicks <= 0)
        {
            var next = !_raining;
            RainTicks = next ? random.Next(12000, 24001) : random.Next(12000, 180001);
            Apply(next, _thundering);
        }

        if (--ThunderTicks <= 0)
        {
            var next = !_thundering;
            ThunderTicks = next ? random.Next(3600, 15601) : random.Next(12000, 180001);
            Apply(_raining, next);
        }
    }

    /// <summary>What <c>/weather clear|rain|thunder</c> does: sets the state and how long it lasts.</summary>
    public void Set(int level, int ticks)
    {
        ticks = Math.Max(1, ticks);
        switch (level)
        {
            case 0:
                RainTicks = ticks;
                ThunderTicks = ticks;
                Apply(false, false);
                break;
            case 1:
                RainTicks = ticks;
                ThunderTicks = Math.Max(ThunderTicks, ticks);
                Apply(true, false);
                break;
            default:
                RainTicks = ticks;
                ThunderTicks = ticks;
                Apply(true, true);
                break;
        }
    }

    /// <summary>Restores a saved state without announcing a change (clients get the current state when they join).</summary>
    public void Restore(bool raining, bool thundering, int rainTicks, int thunderTicks)
    {
        _raining = raining;
        _thundering = thundering;
        RainTicks = Math.Max(1, rainTicks);
        ThunderTicks = Math.Max(1, thunderTicks);
        Interlocked.Increment(ref _version);
    }

    private void Apply(bool raining, bool thundering)
    {
        var shown = Level;
        _raining = raining;
        _thundering = thundering;
        if (Level != shown) Interlocked.Increment(ref _version);
    }
}
