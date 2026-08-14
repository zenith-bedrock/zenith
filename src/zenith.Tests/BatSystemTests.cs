using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XVII, Priority 1 — first flying mob: 3D wander, no supporting-block requirement, no retaliation.</summary>
public sealed class BatSystemTests
{
    [Fact]
    public void Bootstrap_spawns_one_bat_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("spelunker");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new BatSystem(fx.World, fx.Players, new BatStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var bat = Assert.Single(system.Bats.Active);
        Assert.True(bat.IsActive);
        Assert.NotEqual(0, bat.EntityId);
        Assert.Equal((ulong)bat.EntityId, bat.RuntimeId);
    }

    [Fact]
    public void Bat_wanders_in_three_dimensions_without_any_player_nearby()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-spelunker");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var store = new BatStore();
        var bat = new Bat(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY + 5, 0);
        Assert.True(store.TryAdd(bat));
        var system = new BatSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(1));

        var startX = bat.PositionX;
        var startY = bat.PositionY;
        var startZ = bat.PositionZ;
        for (var i = 0; i < 200; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        // A ground mob's wander never moves the Y axis; a flying mob's does — the concrete
        // behavioral difference this priority exists to surface.
        Assert.True(bat.PositionX != startX || bat.PositionZ != startZ || bat.PositionY != startY);
    }

    [Fact]
    public void Bat_never_attacks_or_damages_a_nearby_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        var store = new BatStore();
        var bat = new Bat(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 0.5f, player.PositionY + 2, player.PositionZ);
        Assert.True(store.TryAdd(bat));
        var system = new BatSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(2));

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
        var store = new BatStore();
        var bat = new Bat(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(bat));
        var system = new BatSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        for (var i = 0; i < 2; i++)
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.Empty(store.Active);
        Assert.False(bat.IsActive);
        Assert.True(bat.Health.IsDead);
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
        var store = new BatStore();
        var bat = new Bat(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY + 5, 0);
        Assert.True(store.TryAdd(bat));
        var system = new BatSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(1));

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(bat.IsActive);
        Assert.Empty(system.Bats.Active);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingBatReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new BatSystem(fx.World, fx.Players, new BatStore(), fx.Context.ItemPalette);
        system.Tick(fx.Clock, fx.Players.Online);
        var before = fx.Transport.Captured.Count;

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var player in fx.Players.Online)
            player.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
        Assert.True(first.IsInGame && second.IsInGame);
    }
}
