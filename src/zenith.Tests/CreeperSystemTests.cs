using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XV — Creeper: fuse/explosion is a different death path, sharing only kill bookkeeping.</summary>
public sealed class CreeperSystemTests
{
    [Fact]
    public void Bootstrap_spawns_one_creeper_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bootstrapper");
        var system = new CreeperSystem(fx.World, fx.Players, new CreeperStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var creeper = Assert.Single(system.Creepers.Active);
        Assert.True(creeper.IsActive);
        _ = player;
    }

    [Fact]
    public void Creeper_acquires_a_target_and_starts_fusing_within_ignite_range()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        var store = new CreeperStore();
        var creeper = new Creeper(fx.Players.AllocateRuntimeId(), 99, player.PositionX, player.PositionY, player.PositionZ + 1f);
        Assert.True(store.TryAdd(creeper));
        var system = new CreeperSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(player.RuntimeId, creeper.TargetPlayerRuntimeId);
        Assert.True(creeper.IsFusing);
    }

    [Fact]
    public void Fuse_completes_and_explodes_after_its_full_duration()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("victim");
        var store = new CreeperStore();
        var creeper = new Creeper(fx.Players.AllocateRuntimeId(), 99, player.PositionX, player.PositionY, player.PositionZ + 1f);
        Assert.True(store.TryAdd(creeper));
        var system = new CreeperSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online); // tick 0: ignites
        Assert.True(creeper.IsFusing);

        fx.Clock.AdvanceBy(29);
        system.Tick(fx.Clock, fx.Players.Online); // tick 29: not yet 30 ticks since ignite
        Assert.True(creeper.IsActive);
        Assert.DoesNotContain(store.Active, c => !c.IsActive);

        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online); // tick 30: explodes

        Assert.False(creeper.IsActive);
        Assert.Empty(store.Active);
        Assert.Equal(1, system.ExplosionCount);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:gunpowder"), loot.Id.Value);
        Assert.True(player.Health < 20f); // caught in the blast at 1 block away
        // Self-explosion has no attacker to attribute XP to — DamageSource.Generic carries no owner.
        Assert.Equal(0, player.ExperiencePoints);
    }

    [Fact]
    public void Player_leaving_ignite_range_defuses_the_creeper()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("retreater");
        var store = new CreeperStore();
        var creeper = new Creeper(fx.Players.AllocateRuntimeId(), 99, player.PositionX, player.PositionY, player.PositionZ + 1f);
        Assert.True(store.TryAdd(creeper));
        var system = new CreeperSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(creeper.IsFusing);

        player.PositionZ += 10f; // still within detection (16) but outside ignite (3)
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(creeper.IsFusing);
        Assert.True(creeper.IsActive);
    }

    [Fact]
    public void Melee_kill_before_the_fuse_completes_uses_ordinary_shared_bookkeeping()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("slayer");
        var store = new CreeperStore();
        var creeper = new Creeper(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(creeper));
        var system = new CreeperSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        // One lethal swing, not several graduated ones (Phase XXIII-B: with real hit-invulnerability
        // now enforced, several small melee hits spaced >10 ticks apart cannot land inside the
        // Creeper's own 30-tick fuse anyway once the player is close enough to reach it — that's now
        // correct vanilla-like tension, not a bug. This test is about the *bookkeeping path*, not
        // about racing the fuse, so it deals the kill in one authoritative call.)
        Assert.True(system.TryApplyDamage(
            creeper, DamageSource.MeleeFrom(player.RuntimeId), creeper.Health.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.False(creeper.IsActive);
        Assert.Empty(store.Active);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:gunpowder"), loot.Id.Value);
        Assert.Equal(5, player.ExperiencePoints); // player-caused kill *does* credit XP, unlike self-explosion
        Assert.Equal(0, system.ExplosionCount);
    }

    [Fact]
    public void A_late_joining_player_is_replicated_the_already_active_creeper()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = -1;
        var store = new CreeperStore();
        var creeper = new Creeper(fx.Players.AllocateRuntimeId(), 99, first.PositionX + 5, first.PositionY, first.PositionZ);
        Assert.True(store.TryAdd(creeper));
        var system = new CreeperSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);
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
