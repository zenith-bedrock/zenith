using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XV — Enderman: teleport movement and damage-triggered aggro, sharing only kill bookkeeping.</summary>
public sealed class EndermanSystemTests
{
    [Fact]
    public void Bootstrap_spawns_one_enderman_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("bootstrapper");
        var system = new EndermanSystem(fx.World, fx.Players, new EndermanStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var enderman = Assert.Single(system.Endermen.Active);
        Assert.True(enderman.IsActive);
    }

    [Fact]
    public void Passive_enderman_eventually_teleports_without_ever_being_hit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-observer");
        player.PositionX = 500; // far outside any melee/aggro range
        var store = new EndermanStore();
        var enderman = new Enderman(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY, 0);
        Assert.True(store.TryAdd(enderman));
        var system = new EndermanSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(7));

        var startX = enderman.PositionX;
        var startZ = enderman.PositionZ;
        for (var i = 0; i < 150; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(system.TeleportCount > 0);
        Assert.True(enderman.PositionX != startX || enderman.PositionZ != startZ);
        Assert.Null(enderman.AggroTargetRuntimeId);
    }

    [Fact]
    public void A_landed_player_hit_provokes_the_enderman()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("provoker");
        var store = new EndermanStore();
        var enderman = new Enderman(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(enderman));
        var system = new EndermanSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(player.RuntimeId, enderman.AggroTargetRuntimeId);
        Assert.True(enderman.AggroTicksRemaining > 0);
        Assert.True(enderman.Health.Current < enderman.Health.Maximum);
    }

    [Fact]
    public void Aggravated_enderman_teleports_toward_and_eventually_retaliates_against_its_attacker()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("retaliation-target");
        var store = new EndermanStore();
        // Spawned out of melee range so it must close the distance via teleport before it can hit back.
        var enderman = new Enderman(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 20, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(enderman));
        var system = new EndermanSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(3));

        // Provoke directly (same effect as a landed hit) so this test isolates aggro *movement/retaliation*
        // rather than re-testing the "a hit provokes it" path already covered above.
        enderman.AggroTargetRuntimeId = player.RuntimeId;
        enderman.AggroTicksRemaining = 100;

        var healthBefore = player.Health;
        for (var i = 0; i < 100 && player.Health == healthBefore; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(player.Health < healthBefore);
    }

    [Fact]
    public void Aggro_expires_and_the_target_is_cleared_once_it_counts_down_to_zero()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("forgiven");
        var store = new EndermanStore();
        var enderman = new Enderman(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 30, player.PositionY, player.PositionZ);
        enderman.AggroTargetRuntimeId = player.RuntimeId;
        enderman.AggroTicksRemaining = 1;
        Assert.True(store.TryAdd(enderman));
        var system = new EndermanSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, enderman.AggroTicksRemaining);
        Assert.Null(enderman.AggroTargetRuntimeId);
    }

    [Fact]
    public void Melee_kill_drops_an_ender_pearl_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("slayer");
        var store = new EndermanStore();
        var enderman = new Enderman(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(enderman));
        var system = new EndermanSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        for (var i = 0; i < 3; i++)
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.False(enderman.IsActive);
        Assert.Empty(store.Active);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:ender_pearl"), loot.Id.Value);
        Assert.Equal(5, player.ExperiencePoints);
    }

    [Fact]
    public void Attack_outside_reach_does_not_mutate_enderman_health()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var store = new EndermanStore();
        var enderman = new Enderman(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 10, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(enderman));
        var system = new EndermanSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(enderman.Health.Maximum, enderman.Health.Current);
        Assert.Single(store.Active);
    }

    [Fact]
    public void A_late_joining_player_is_replicated_the_already_active_enderman()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = -1;
        var store = new EndermanStore();
        var enderman = new Enderman(fx.Players.AllocateRuntimeId(), 99, first.PositionX + 5, first.PositionY, first.PositionZ);
        Assert.True(store.TryAdd(enderman));
        var system = new EndermanSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        var second = fx.AddInGamePlayer("late-joiner");
        second.Chunks.Radius = -1;
        second.PositionX = first.PositionX;
        second.PositionZ = first.PositionZ;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, system.ReplicatedSpawnCount);
    }
}
