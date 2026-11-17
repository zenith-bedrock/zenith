using Xunit;
using Zenith.Gameplay.Replication;
using Zenith.Rulebook;

namespace Zenith.Tests;

/// <summary>Plan 005 phase B: the world's settings (game rules, difficulty, weather) share one replication and storage path.</summary>
public sealed class WorldStatesTests
{
    [Fact]
    public void The_world_has_exactly_the_three_bindings_and_each_has_its_own_name()
    {
        var fx = new IntentTestFixture();
        Assert.Equal(["gamerules", "difficulty", "weather"], WorldStates.For(fx.World).Select(s => s.Name));
    }

    [Fact]
    public void Each_setting_is_sent_once_to_every_player_when_it_changes_and_nothing_is_sent_when_it_does_not()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("a");
        var b = fx.AddInGamePlayer("b");
        var (ca, cb) = (new RecordingClientChannel(), new RecordingClientChannel());
        a.AttachClient(ca);
        b.AttachClient(cb);
        var sync = new WorldStateSyncSystem(fx.World);

        sync.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(ca.Calls);

        fx.World.Difficulty.Set(Difficulty.Hard);
        fx.World.Weather.Set(1, 500);
        Assert.True(fx.World.Rules.TrySet("keepInventory", "true", out _));
        sync.Tick(fx.Clock, fx.Players.Online);
        sync.Tick(fx.Clock, fx.Players.Online);

        foreach (var client in new[] { ca, cb })
        {
            Assert.Single(client.Of("SendDifficulty"));
            Assert.Single(client.Of("SendWeather"));
            Assert.Single(client.Of("SendGameRules"));
        }
    }

    [Fact]
    public void A_restart_restores_every_setting_through_the_same_path()
    {
        var storage = new World.InMemoryChunkStorage();
        var first = new World.World(storage);
        Assert.True(first.Rules.TrySet("keepInventory", "true", out _));
        first.Difficulty.Set(Difficulty.Easy);
        first.Weather.Set(2, 321);
        foreach (var state in WorldStates.For(first)) state.Save(first);

        var second = new World.World(storage);
        WorldStates.LoadAll(second);

        Assert.True(second.Rules.KeepInventory);
        Assert.Equal(Difficulty.Easy, second.Difficulty.Value);
        Assert.Equal(2, second.Weather.Level);
        Assert.Equal(321, second.Weather.RainTicks);
    }

    [Fact]
    public void A_world_with_nothing_stored_keeps_the_defaults_and_draws_weather_timers()
    {
        var world = new World.World(new World.InMemoryChunkStorage());
        WorldStates.LoadAll(world);

        Assert.Equal(Difficulty.Normal, world.Difficulty.Value);
        Assert.False(world.Weather.Raining);
        Assert.InRange(world.Weather.RainTicks, 12000, 180000);
    }
}
