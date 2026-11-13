using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// The viewer set of an actor is re-derived only against the players whose interest changed, unless the actor changed chunk.
/// These tests pin that every way a player's view of an actor can change is still noticed.
/// </summary>
public sealed class ViewerInterestTests
{
    [Fact]
    public void A_player_seen_for_the_first_time_counts_as_changed_and_an_unchanged_one_does_not()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("a");
        var tracker = new ViewerInterestTracker();

        tracker.Begin(fx.Players.Online);
        Assert.Contains(player, tracker.Changed);

        tracker.Begin(fx.Players.Online);
        Assert.Empty(tracker.Changed);
    }

    [Fact]
    public void A_change_in_known_columns_view_radius_or_liveness_marks_the_player_changed()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("a");
        player.Chunks.Radius = 2;
        var tracker = new ViewerInterestTracker();
        tracker.Begin(fx.Players.Online);

        player.Chunks.RememberMany([(3, 3)]);
        tracker.Begin(fx.Players.Online);
        Assert.Contains(player, tracker.Changed);

        tracker.Begin(fx.Players.Online);
        Assert.Empty(tracker.Changed);

        player.Chunks.Forget(3, 3);
        tracker.Begin(fx.Players.Online);
        Assert.Contains(player, tracker.Changed);

        player.Chunks.Radius = 4;
        tracker.Begin(fx.Players.Online);
        Assert.Contains(player, tracker.Changed);

        player.IsInGame = false;
        tracker.Begin(fx.Players.Online);
        Assert.Contains(player, tracker.Changed);
    }

    [Fact]
    public void A_player_leaving_does_not_mark_the_remaining_players_changed()
    {
        var fx = new IntentTestFixture();
        var stays = fx.AddInGamePlayer("stays");
        var leaves = fx.AddInGamePlayer("leaves");
        var tracker = new ViewerInterestTracker();
        tracker.Begin(fx.Players.Online);

        fx.Players.Remove(leaves);
        tracker.Begin(fx.Players.Online);
        Assert.Empty(tracker.Changed);
        Assert.DoesNotContain(stays, tracker.Changed);
    }

    [Fact]
    public void A_column_learned_after_the_actor_settled_still_gets_the_actor_replicated()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("watcher");
        player.Chunks.Radius = 1; // bounded view: sees only the columns it knows
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        system.SpawnCow(0.5f, Blocks.FlatSpawnY, 0.5f);

        system.Tick(fx.Clock, fx.Players.Online);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.ReplicatedSpawnCount);

        player.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.ReplicatedSpawnCount);
    }

    [Fact]
    public void A_forgotten_column_removes_the_actor_from_a_viewer_whose_actor_did_not_move()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("watcher");
        player.Chunks.Radius = 1;
        player.Chunks.RememberMany([(0, 0)]);
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        system.SpawnCow(0.5f, Blocks.FlatSpawnY, 0.5f);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        player.Chunks.Forget(0, 0);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.ReplicatedRemovalCount);
    }

    [Fact]
    public void A_viewer_that_stops_being_in_game_loses_the_actor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("watcher");
        player.Chunks.Radius = -1;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        system.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        player.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.ReplicatedRemovalCount);
    }

    [Fact]
    public void Reconciling_before_the_tick_began_fails_loudly()
    {
        var fx = new IntentTestFixture();
        var stores = new EntityRuntime();
        var replication = new ActorReplication(stores, Zenith.Gameplay.Ports.MobKind.Cow);
        var id = stores.CreateActor(1, 1, 0, 0, 0) ?? throw new InvalidOperationException();

        Assert.Throws<InvalidOperationException>(() => replication.Reconcile(id, fx.Players.Online));
    }
}
