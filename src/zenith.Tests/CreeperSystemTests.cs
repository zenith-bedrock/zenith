using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>
/// Phase XV — Creeper: fuse/explosion is a different death path, sharing only kill bookkeeping.
/// ECS-authoritative since the Creeper/Enderman/Golem/Fish migration (ADR §132).
/// </summary>
public sealed class CreeperSystemTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Late_viewer_receives_current_fuse_after_spawn_without_resending_to_existing_viewer(bool fusing)
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first-fuse");
        first.Chunks.Radius = -1;
        var firstClient = new RecordingClientChannel(); first.AttachClient(firstClient);
        var system = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        var id = system.SpawnCreeper(first.PositionX, first.PositionY, first.PositionZ + (fusing ? 1f : 10f));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(fusing, IsFusing(system, id));
        firstClient.Calls.Clear();

        var second = fx.AddInGamePlayer("late-fuse");
        second.Chunks.Radius = -1;
        var secondClient = new RecordingClientChannel(); second.AttachClient(secondClient);
        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.Stores.Identities.TryGet(id, out var identity));
        var spawnIndex = secondClient.Calls.FindIndex(c => c.Call == "SendAddMob");
        Assert.True(spawnIndex >= 0);
        var ignited = secondClient.Calls.Where(c => c.Call == "SendActorIgnited").ToArray();
        if (fusing)
        {
            var call = Assert.Single(ignited);
            Assert.Equal(identity.ActorRuntimeId, Assert.IsType<ulong>(call.Args[0]));
            Assert.True(Assert.IsType<bool>(call.Args[1]));
            Assert.True(secondClient.Calls.FindIndex(c => c.Call == "SendActorIgnited") > spawnIndex);
        }
        else Assert.Empty(ignited);
        Assert.DoesNotContain(firstClient.Calls, c => c.Call == "SendActorIgnited");
    }

    private static long? Target(CreeperSystem system, EntityId id)
    {
        Assert.True(system.CreeperStates.TryGet(id, out var state));
        return state.TargetPlayerRuntimeId;
    }

    private static bool IsFusing(CreeperSystem system, EntityId id)
    {
        Assert.True(system.CreeperStates.TryGet(id, out var state));
        return state.IsFusing;
    }

    [Fact]
    public void Creeper_acquires_a_target_and_starts_fusing_within_ignite_range()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        var system = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        var id = system.SpawnCreeper(player.PositionX, player.PositionY, player.PositionZ + 1f);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(player.RuntimeId, Target(system, id));
        Assert.True(IsFusing(system, id));
    }

    [Fact]
    public void Fuse_completes_and_explodes_after_its_full_duration()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("victim");
        var system = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        var id = system.SpawnCreeper(player.PositionX, player.PositionY, player.PositionZ + 1f);

        system.Tick(fx.Clock, fx.Players.Online); // tick 0: ignites
        Assert.True(IsFusing(system, id));

        fx.Clock.AdvanceBy(29);
        system.Tick(fx.Clock, fx.Players.Online); // tick 29: not yet 30 ticks since ignite
        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.Contains(id, system.Creepers);

        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online); // tick 30: explodes

        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Creepers);
        Assert.Equal(1, system.ExplosionCount);
        LootAssert.OnlyWhatTheTableAwards(fx.World, fx.Context.ItemPalette, "creeper");
        Assert.True(player.Health < 20f); // caught in the blast at 1 block away
        // Self-explosion has no attacker to attribute XP to — DamageSource.Generic carries no owner.
        Assert.Equal(0, player.ExperiencePoints);
    }

    [Fact]
    public void Player_leaving_ignite_range_defuses_the_creeper()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("retreater");
        var system = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        var id = system.SpawnCreeper(player.PositionX, player.PositionY, player.PositionZ + 1f);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(IsFusing(system, id));

        player.PositionZ += 10f; // still within detection (16) but outside ignite (3)
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(IsFusing(system, id));
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void Melee_kill_before_the_fuse_completes_uses_ordinary_shared_bookkeeping()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("slayer");
        var system = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        var id = system.SpawnCreeper(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(system.Stores.Health.TryGet(id, out var healthComponent));
        var maxHealth = healthComponent.State.Maximum;

        // One lethal swing, not several graduated ones (Phase XXIII-B: with real hit-invulnerability
        // now enforced, several small melee hits spaced >10 ticks apart cannot land inside the
        // Creeper's own 30-tick fuse anyway once the player is close enough to reach it — that's now
        // correct vanilla-like tension, not a bug. This test is about the *bookkeeping path*, not
        // about racing the fuse, so it deals the kill in one authoritative call.)
        Assert.True(system.TryApplyDamage(
            id, DamageSource.MeleeFrom(player.RuntimeId), maxHealth, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Creepers);
        LootAssert.OnlyWhatTheTableAwards(fx.World, fx.Context.ItemPalette, "creeper");
        Assert.Equal(5, player.ExperiencePoints); // player-caused kill *does* credit XP, unlike self-explosion
        Assert.Equal(0, system.ExplosionCount);
    }

    [Fact]
    public void A_late_joining_player_is_replicated_the_already_active_creeper()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = -1;
        var system = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        system.SpawnCreeper(first.PositionX + 5, first.PositionY, first.PositionZ);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        var second = fx.AddInGamePlayer("late-joiner");
        second.Chunks.Radius = -1;
        second.PositionX = first.PositionX;
        second.PositionZ = first.PositionZ;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, system.ReplicatedSpawnCount);
    }

    [Fact]
    public void Arrow_can_damage_a_creeper_through_the_shared_dispatch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var stores = new EntityRuntime();
        var creepers = new CreeperSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        var damage = new DamageDispatch();
        damage.Register(creepers.Owns, creepers.TryApplyDamage);
        var projectiles = new ProjectileSystem(fx.World, fx.Players, stores, damage, new PlayerSpatialIndex());
        var id = creepers.SpawnCreeper(10f, 100f, 0f); // 20 HP; a 4-damage hit must leave it alive but wounded, not untouched.
        Assert.True(stores.Health.TryGet(id, out var before));
        var maxHealth = before.State.Maximum;

        projectiles.TrySpawnFromActor(owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f, fx.Players.Online);
        projectiles.Tick(fx.Clock, fx.Players.Online);

        Assert.True(stores.Health.TryGet(id, out var after));
        Assert.True(after.State.Current < maxHealth); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(projectiles.Projectiles);
    }
}
