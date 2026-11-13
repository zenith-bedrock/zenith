using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>The flying mob: 3D wander, no supporting-block requirement, no retaliation. An ECS species like every other.</summary>
public sealed class BatSystemTests
{
    private static Position Pos(BatSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    [Fact]
    public void Bat_wanders_in_three_dimensions_without_any_player_nearby()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-spelunker");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new BatSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnBat(0, Blocks.FlatSpawnY + 5, 0);

        var start = Pos(system, id);
        for (var i = 0; i < 200; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        // A ground mob's wander never moves the Y axis; a flying mob's does.
        var end = Pos(system, id);
        Assert.True(end.X != start.X || end.Z != start.Z || end.Y != start.Y);
    }

    /// <summary>BatSystem never calls <see cref="GroundMobMovement"/>: no gravity and no fall-speed, so a bat suspended mid-air stays there.</summary>
    [Fact]
    public void Bat_flight_is_not_affected_by_ground_gravity()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-spelunker");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new BatSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(4));
        var id = system.SpawnBat(0, Blocks.FlatSpawnY + 20, 0);

        for (var i = 0; i < 100; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        // A ground actor left this high with no support would fall ~4 blocks/tick and hit terrain long before 100 ticks.
        Assert.True(Pos(system, id).Y > Blocks.FlatSpawnY);
        Assert.False(system.Stores.Velocities.Has(id));
    }

    [Fact]
    public void Bat_never_attacks_or_damages_a_nearby_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        var system = new BatSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(2));
        system.SpawnBat(player.PositionX + 0.5f, player.PositionY + 2, player.PositionZ);

        for (var i = 0; i < 100; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Health);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void Melee_kill_drops_feather_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("hunter");
        var system = new BatSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnBat(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 2; i++)
        {
            player.Intents.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.Empty(system.Bats);
        Assert.False(system.Stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:feather"), loot.Id.Value);
        Assert.Equal(1, player.ExperiencePoints);
    }

    [Fact]
    public void A_bat_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-spelunker");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new BatSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnBat(0, Blocks.FlatSpawnY + 5, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Bats);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingBatReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new BatSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnBat(8, Blocks.FlatSpawnY + 5, 8); // mid-column, so a wander step cannot leave the known column
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, system.ReplicatedSpawnCount);
        Assert.True(first.IsInGame && second.IsInGame);
    }

    [Fact]
    public void A_bat_can_be_damaged_through_the_shared_dispatch_like_any_other_species()
    {
        var fx = new IntentTestFixture();
        var shooter = fx.AddInGamePlayer("archer");
        var system = new BatSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnBat(shooter.PositionX + 1, shooter.PositionY, shooter.PositionZ);
        var dispatch = new DamageDispatch();
        dispatch.Register(system.Owns, system.TryApplyDamage);

        Assert.True(dispatch.TryApplyDamage(id, DamageSource.Projectile(shooter.RuntimeId), 100f, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.False(system.Stores.Entities.IsAlive(id));
    }
}
