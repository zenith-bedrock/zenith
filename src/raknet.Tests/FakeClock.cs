namespace Zenith.Raknet.Tests;

/// <summary>A <see cref="TimeProvider"/> whose time only moves when a test says so.</summary>
internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
