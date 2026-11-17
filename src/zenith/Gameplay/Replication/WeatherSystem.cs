using Zenith.Gameplay.Runtime;

namespace Zenith.Gameplay.Replication;

/// <summary>
/// Runs the weather cycle (when the <c>doWeatherCycle</c> rule is on) and replicates it: a change of rain or thunder is sent to every
/// online player as the LevelEvents Bedrock uses, and stored. A player who joins later is told by the join sequence.
/// </summary>
sealed class WeatherSystem(World.World world, Random? random = null) : IGameSystem
{
    private const int SaveIntervalTicks = 1200;

    private readonly Random _random = random ?? Random.Shared;
    private long _published = world.Weather.Version;

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var weather = world.Weather;
        if (world.Rules.DoWeatherCycle) weather.Advance(_random);

        var version = weather.Version;
        if (version != _published)
        {
            _published = version;
            foreach (var player in online)
                player.Client.World.SendWeather(weather.Raining, weather.Thunder);
            world.PersistWeather();
        }
        else if (clock.CurrentTick % SaveIntervalTicks == 0)
            world.PersistWeather();
    }
}
