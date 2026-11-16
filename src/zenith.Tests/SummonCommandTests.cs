using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Entities;
using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

public sealed class SummonCommandTests
{
    [Fact]
    public void Summon_is_queued_and_spawns_inside_the_tick_at_the_players_position()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddOperator("operator");
        player.PositionX = 3;
        player.PositionY = 70;
        player.PositionZ = -4;
        var spawner = new MobSpawner(fx.Context.Gate);
        (float X, float Y, float Z)? spawned = null;
        spawner.Register("zombie", (x, y, z) => spawned = (x, y, z));
        var commands = new CommandRuntime(fx.Players, fx.Context.Permissions, spawner: spawner);

        var feedback = commands.Execute(player, "/summon zombie");

        Assert.Contains("Queued zombie", feedback.Message); // accepted, not yet applied
        Assert.Null(spawned); // nothing happens until the game loop drains the queue
        spawner.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal((3f, 70f, -4f), spawned);
    }

    [Fact]
    public void Summon_rejects_an_unknown_mob_and_does_not_exist_without_a_spawner()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddOperator("operator");
        var spawner = new MobSpawner(fx.Context.Gate);
        spawner.Register("cow", (_, _, _) => { });

        Assert.Equal("Usage: /summon", new CommandRuntime(fx.Players, fx.Context.Permissions, spawner: spawner).Execute(player, "/summon dragon").Message);
        Assert.Equal("Unknown command.", new CommandRuntime(fx.Players, fx.Context.Permissions).Execute(player, "/summon cow").Message);
        Assert.False(spawner.Request("dragon", 0, 0, 0));
    }

    [Fact]
    public void Registering_the_same_mob_name_twice_is_an_error()
    {
        var spawner = new MobSpawner();
        spawner.Register("cow", (_, _, _) => { });
        Assert.Throws<InvalidOperationException>(() => spawner.Register("COW", (_, _, _) => { }));
    }

    [Fact]
    public void A_default_player_cannot_summon()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("plain");
        var spawner = new MobSpawner(fx.Context.Gate);
        var spawned = false;
        spawner.Register("zombie", (_, _, _) => spawned = true);
        var commands = new CommandRuntime(fx.Players, fx.Context.Permissions, spawner: spawner);

        Assert.Equal("You do not have permission.", commands.Execute(player, "/summon zombie").Message);
        spawner.Tick(fx.Clock, fx.Players.Online);
        Assert.False(spawned);
    }

    [Fact]
    public void A_summon_queued_before_the_permission_was_revoked_does_not_spawn()
    {
        var fx = new IntentTestFixture();
        var admin = fx.AddOperator("admin");
        var spawner = new MobSpawner(fx.Context.Gate);
        var spawned = false;
        spawner.Register("zombie", (_, _, _) => spawned = true);
        var commands = new CommandRuntime(fx.Players, fx.Context.Permissions, spawner: spawner);
        Assert.Contains("Queued", commands.Execute(admin, "/summon zombie").Message);

        fx.Context.Permissions.Replace(Zenith.Gameplay.Permissions.PermissionPolicy.Empty);
        spawner.Tick(fx.Clock, fx.Players.Online);

        Assert.False(spawned);
    }
}
