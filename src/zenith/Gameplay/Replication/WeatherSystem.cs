using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Runs the weather cycle while the <c>doWeatherCycle</c> rule is on. A change of rain or thunder is replicated and stored by
/// <see cref="WorldStateSyncSystem"/> like any world setting; this system only moves the timers, and saves them now and then so a restart
/// resumes close to where the cycle was.
/// </summary>
[GameSystem("weather")]
sealed class WeatherSystem(World.World world, Random? random = null) : IGameSystem
{
    private const int SaveIntervalTicks = 1200;

    private readonly Random _random = random ?? Random.Shared;

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (world.Rules.DoWeatherCycle) world.Weather.Advance(_random);
        if (clock.CurrentTick % SaveIntervalTicks == 0) WorldStates.Save(world, "weather");
    }
}
