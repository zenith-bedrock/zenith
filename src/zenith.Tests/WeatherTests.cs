using Xunit;
using Zenith.Gameplay.Commands;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction.Fire;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 26: rain and thunder, the cycle, the wire, and what the weather does to light, fire and the sun.</summary>
public sealed class WeatherTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public WeatherTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Rain_and_a_storm_leave_eleven_sixteenths_of_the_day_each()
    {
        Assert.Equal(0, LightModel.SkyDarkness(6000, 0));
        Assert.Equal(3, LightModel.SkyDarkness(6000, 1));
        Assert.Equal(5, LightModel.SkyDarkness(6000, 2));
        Assert.Equal(11, LightModel.SkyDarkness(18000, 2));
    }

    [Fact]
    public void A_command_sets_the_weather_and_a_storm_needs_rain()
    {
        var weather = new WeatherState();
        weather.Set(2, 100);
        Assert.True(weather.Raining && weather.Thunder);
        weather.Set(1, 100);
        Assert.True(weather.Raining && !weather.Thunder);
        weather.Set(0, 100);
        Assert.False(weather.Raining);
        Assert.Equal(0, weather.Level);
    }

    [Fact]
    public void The_cycle_flips_rain_when_its_timer_runs_out_and_draws_a_vanilla_length()
    {
        var weather = new WeatherState();
        weather.Restore(raining: false, thundering: false, rainTicks: 3, thunderTicks: 100000);
        var random = new Random(1);
        for (var i = 0; i < 3; i++) weather.Advance(random);
        Assert.True(weather.Raining);
        Assert.InRange(weather.RainTicks, 12000, 24000);
    }

    [Fact]
    public void The_system_runs_the_cycle_only_while_the_rule_is_on_and_tells_players_once_per_change()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("watcher");
        var client = new RecordingClientChannel();
        player.AttachClient(client);
        fx.World.Weather.Restore(false, false, 2, 100000);
        var system = new WeatherSystem(fx.World, new Random(1));
        var sync = new WorldStateSyncSystem(fx.World);

        Assert.True(fx.World.Rules.TrySet("doWeatherCycle", "false", out _));
        for (var i = 0; i < 5; i++) { system.Tick(fx.Clock, fx.Players.Online); sync.Tick(fx.Clock, fx.Players.Online); }
        Assert.False(fx.World.Weather.Raining);
        Assert.Empty(client.Of("SendWeather"));

        Assert.True(fx.World.Rules.TrySet("doWeatherCycle", "true", out _));
        for (var i = 0; i < 5; i++) { system.Tick(fx.Clock, fx.Players.Online); sync.Tick(fx.Clock, fx.Players.Online); }
        Assert.True(fx.World.Weather.Raining);
        Assert.Equal([true, false], client.Of("SendWeather").Single());
    }

    [Fact]
    public void The_weather_command_changes_and_reports_it_and_needs_operator()
    {
        var fx = new IntentTestFixture();
        var op = fx.AddOperator("op");
        var pleb = fx.AddInGamePlayer("pleb");
        var commands = new CommandRuntime(fx.Players, fx.Context.Permissions, weather: fx.World.Weather);

        commands.Execute(pleb, "/weather rain");
        Assert.False(fx.World.Weather.Raining);

        commands.Execute(op, "/weather thunder");
        Assert.Equal(2, fx.World.Weather.Level);
        Assert.Contains("thunder", commands.Execute(op, "/weather query").Message);
        commands.Execute(op, "/weather clear 500");
        Assert.Equal(0, fx.World.Weather.Level);
    }

    [Fact]
    public void The_weather_survives_a_restart()
    {
        var storage = new InMemoryChunkStorage();
        var first = new World.World(storage);
        first.Weather.Set(2, 777);
        WorldStates.Save(first, "weather");

        var second = new World.World(storage);
        WorldStates.LoadAll(second);

        Assert.Equal(2, second.Weather.Level);
        Assert.Equal(777, second.Weather.RainTicks);
    }

    private static async Task<IntentTestFixture> Stage()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 12; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        return fx;
    }

    [Fact]
    public async Task Rain_dims_the_open_sky()
    {
        var fx = await Stage();
        Assert.Equal(15, LightModel.Brightness(fx.World, 8, Level, 8, 6000));
        fx.World.Weather.Set(2, 1000);
        Assert.Equal(10, LightModel.Brightness(fx.World, 8, Level, 8, 6000));
    }

    [Fact]
    public async Task Rain_puts_out_a_fire_under_the_open_sky_more_than_one_under_a_roof()
    {
        var fx = await Stage();
        fx.World.Weather.Set(1, 100000);
        for (var x = 0; x < 16; x++)
            for (var z = 8; z < 16; z++) fx.World.SetBlock(x, Level + 3, z, Blocks.Stone);
        var changes = new List<(int X, int Y, int Z, int BlockRuntimeId)>();
        var fire = new FireSpread(fx.World, new Random(3), changes, fx.Context.EnvironmentPolicies);
        var extinguished = 0;
        var roofedKept = 0;
        for (var i = 0; i < 30; i++)
        {
            fx.World.SetBlock(2, Level, 2, Blocks.Fire);
            fx.World.SetBlock(2, Level, 12, Blocks.Fire);
            fire.Tick(2, Level, 2);
            fire.Tick(2, Level, 12);
            if (!fx.World.TryGetBlock(2, Level, 2, out var open) || !Blocks.IsFire(open)) extinguished++;
            if (fx.World.TryGetBlock(2, Level, 12, out var roofed) && Blocks.IsFire(roofed)) roofedKept++;
        }

        Assert.True(extinguished > 3, $"extinguished {extinguished}");
        Assert.True(roofedKept > extinguished, $"roofed kept {roofedKept}");
    }

    [Fact]
    public async Task Rain_puts_out_a_burning_player_in_the_open_but_not_under_a_roof()
    {
        var fx = await Stage();
        var player = fx.AddInGamePlayer("walker");
        (player.PositionX, player.PositionY, player.PositionZ) = (8.5f, Level, 8.5f);
        player.BurnTicks = 100;
        var system = new BurnSystem(fx.Players, fx.World);
        fx.World.Weather.Set(1, 100000);

        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, player.BurnTicks);

        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++) fx.World.SetBlock(x, Level + 3, z, Blocks.Stone);
        player.BurnTicks = 100;
        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.BurnTicks > 0);
    }

    [Fact]
    public async Task A_zombie_does_not_burn_in_the_sun_while_it_rains()
    {
        var fx = await Stage();
        var stores = new EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies);
        var id = zombies.SpawnZombie(8.5f, Level, 8.5f);
        var hazard = new MobHazardSystem(fx.World, stores, damage, _ => false, zombies.Owns);
        fx.World.Weather.Set(1, 100000);
        fx.Clock.AdvanceBy(2000);

        for (var i = 0; i < 20; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Equal(0, hazard.BurnTicksOf(id));
    }
}
