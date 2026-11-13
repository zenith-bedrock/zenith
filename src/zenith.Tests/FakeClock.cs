namespace Zenith.Tests;

/// <summary>A <see cref="TimeProvider"/> whose time only moves when a test says so.</summary>
internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public FakeClock() : this(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
