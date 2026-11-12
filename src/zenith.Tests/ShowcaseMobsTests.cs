using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Xunit;

namespace Zenith.Tests;

public sealed class ShowcaseMobsTests
{
    [Fact]
    public void Showcase_off_spawns_no_cow_near_a_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farmer");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, showcase: new ShowcaseMobs(false));

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(system.Cows);
    }

    [Fact]
    public void Showcase_on_by_default_spawns_a_cow_near_a_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farmer");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, showcase: new ShowcaseMobs());

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Single(system.Cows);
    }

    [Fact]
    public void Config_defaults_showcase_on_and_the_key_is_world_showcase_mobs()
    {
        Assert.True(new Zenith.Server.ServerConfig().World.ShowcaseMobs);
    }
}
