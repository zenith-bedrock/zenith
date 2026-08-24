using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>
/// Phase XXI — Zombie is the first ECS-authoritative actor: Position/Health/Velocity live in
/// <see cref="EntityRuntime"/>, ZombieState (target, attack cooldown) is feature-specific. Tests
/// read state through <see cref="ZombieSystem.Stores"/>/<see cref="ZombieSystem.ZombieStates"/>
/// instead of a concrete <c>Zombie</c> object's properties — see
/// docs/history/phases/phase-xxi-ecs-foundation-findings.md for the DX comparison this made visible.
/// </summary>
public sealed class ZombieSystemTests
{
    private static Position Pos(ZombieSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static HealthState Health(ZombieSystem system, EntityId id)
    {
        Assert.True(system.Stores.Health.TryGet(id, out var health));
        return health.State;
    }

    private static long? Target(ZombieSystem system, EntityId id)
    {
        Assert.True(system.ZombieStates.TryGet(id, out var state));
        return state.TargetPlayerRuntimeId;
    }

    [Fact]
    public void BootstrapSpawnsOneConcreteZombieAndMovesTowardPlayer()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Zombies);
        var initialDistance = MathF.Abs(Pos(system, id).X - player.PositionX);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.True(MathF.Abs(Pos(system, id).X - player.PositionX) < initialDistance);
    }

    [Fact]
    public void Zombie_retargets_when_retained_player_becomes_invalid()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first-target");
        var second = fx.AddInGamePlayer("second-target");
        first.PositionX = 0;
        second.PositionX = 10;
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(4, first.PositionY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(first.RuntimeId, Target(system, id));

        first.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(second.RuntimeId, Target(system, id));
        first.IsInGame = true;
    }

    [Fact]
    public void Zombie_drops_target_when_no_valid_player_remains()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-target");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(4, player.PositionY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(player.RuntimeId, Target(system, id));
        player.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Null(Target(system, id));
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void Zombie_skips_projection_when_authoritative_position_is_unchanged()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stationary-target");
        player.Chunks.Radius = -1;
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnZombie(1, player.PositionY, 0);

        system.Tick(fx.Clock, fx.Players.Online); // spawn and initial projection state
        system.Tick(fx.Clock, fx.Players.Online); // attack range: no movement to project

        Assert.Equal(0, system.ReplicatedMoveCount);
        Assert.True(system.ReplicatedMoveSkippedCount > 0);
    }

    [Fact]
    public void Zombie_chooses_bounded_local_side_step_around_solid_obstacle()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("blocked-target");
        player.PositionX = -2;
        player.PositionZ = 0;
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(2.5f, player.PositionY, 0);
        // A full-height obstacle blocks the direct X route while the neighbouring Z cells remain
        // supported by the flat floor.
        fx.World.SetBlock(1, (int)player.PositionY, 0, Blocks.Stone);
        fx.World.SetBlock(1, (int)player.PositionY + 1, 0, Blocks.Stone);

        for (var i = 0; i < 20; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        var pos = Pos(system, id);
        Assert.NotEqual(0f, pos.Z);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(
            (int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z)));
    }

    [Fact]
    public void AttackIsValidatedByGameplayOwnerAndDeathRemovesExactlyOnce()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, Health(system, id).Current);

        for (var i = 0; i < 4; i++)
        {
            player.SubmitAttackIntent();
            fx.Clock.AdvanceBy(11);
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Empty(system.Zombies);
        Assert.False(system.Stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:rotten_flesh"), loot.Id.Value);
        Assert.True(loot.Id.IsItem);
        Assert.Equal(1, loot.Count);
        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(system.Zombies);
    }

    [Fact]
    public void AttackOutsideReachDoesNotMutateZombieHealth()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(player.PositionX + 10, player.PositionY, player.PositionZ);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        var health = Health(system, id);
        Assert.Equal(health.Maximum, health.Current);
        Assert.Single(system.Zombies);
    }

    [Fact]
    public void Targeted_attack_only_damages_the_runtime_id_named_by_the_client()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var first = system.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        var second = system.SpawnZombie(player.PositionX + 2, player.PositionY, player.PositionZ);
        Assert.True(system.Stores.Identities.TryGet(second, out var secondIdentity));

        player.SubmitAttackIntent(checked((long)secondIdentity.ActorRuntimeId));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, Health(system, first).Current);
        Assert.Equal(16f, Health(system, second).Current);
    }

    /// <summary>
    /// Regression for ADR §135: loot capacity must never veto a kill. A saturated floor-drop store
    /// used to refuse the entire fatal transition, leaving the zombie stuck alive at full health —
    /// a real, server-wide "this mob is now unkillable" bug, not just a lost drop. The kill must
    /// land regardless; only the loot silently fails to appear.
    /// </summary>
    [Fact]
    public void LethalDamage_landsWithoutLootWhenLootCannotBeStored()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);

        // A full store cannot accept a new rotten-flesh cell anywhere, including near the zombie.
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(i + 100, (int)player.PositionY, 0,
                StackId.FromBlock(Blocks.Dirt), 1, fx.Players.AllocateRuntimeId(), out _));

        var maxHealth = Health(system, id).Maximum;
        Assert.True(system.TryApplyDamage(id, DamageSource.Melee, maxHealth, fx.Players.Online, fx.Clock.CurrentTick));
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Equal(FloorDropStore.SoftCap, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void ZombieInReach_damagesPlayerOncePerCooldown()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, player.Health);
        for (var i = 0; i < 19; i++)
        {
            fx.Clock.Advance();
            system.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.Equal(16f, player.Health);
        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(12f, player.Health);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingZombieReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
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

    [Fact]
    public void A_landed_player_hit_knocks_the_zombie_back()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("puncher");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        var beforeX = Pos(system, id).X;

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        // Knocked further from the player (+X direction) than a chase step alone would move it.
        Assert.True(Pos(system, id).X > beforeX);
    }

    [Fact]
    public void A_zombie_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("far-away");
        player.PositionX = 1000; // outside the 64-block despawn radius from tick zero
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(0, player.PositionY, 0);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks - 1);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.Single(system.Zombies);

        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Zombies);
        Assert.Equal(1, system.DespawnCount);
    }

    [Fact]
    public void A_zombie_kept_within_range_of_a_player_never_despawns()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("companion");
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(player.PositionX + 30, player.PositionY, player.PositionZ);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks + 100);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.Equal(0, system.DespawnCount);
    }

    /// <summary>Phase XXIII — hurt/death feedback proof, sharing DamageableActorCombat's seam with every other ECS-damageable species.</summary>
    [Fact]
    public void Non_lethal_damage_to_a_replicated_zombie_sends_a_hurt_reaction_and_leaves_it_alive()
    {
        var fx = new IntentTestFixture();
        var viewer = fx.AddInGamePlayer("viewer");
        viewer.Chunks.Radius = 1;
        viewer.Chunks.RememberMany([(0, 0)]);
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(1f, viewer.PositionY, viewer.PositionZ);
        system.Tick(fx.Clock, fx.Players.Online); // replicate spawn to the viewer before damaging
        foreach (var player in fx.Players.Online) player.Session.RakSession.Tick();
        var before = fx.Transport.Captured.Count;

        Assert.True(system.TryApplyDamage(id, DamageSource.Melee, 6f, fx.Players.Online, fx.Clock.CurrentTick));
        foreach (var player in fx.Players.Online) player.Session.RakSession.Tick();

        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.True(fx.Transport.Captured.Count > before); // health + hurt reaction reached the viewer
    }

    [Fact]
    public void Lethal_damage_sends_death_reaction_before_removal_and_destroys_the_zombie()
    {
        var fx = new IntentTestFixture();
        var viewer = fx.AddInGamePlayer("viewer");
        viewer.Chunks.Radius = 1;
        viewer.Chunks.RememberMany([(0, 0)]);
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(1f, viewer.PositionY, viewer.PositionZ);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var player in fx.Players.Online) player.Session.RakSession.Tick();
        var maxHealth = Health(system, id).Maximum;
        var before = fx.Transport.Captured.Count;

        Assert.True(system.TryApplyDamage(id, DamageSource.Melee, maxHealth, fx.Players.Online, fx.Clock.CurrentTick));
        foreach (var player in fx.Players.Online) player.Session.RakSession.Tick();

        Assert.False(system.Stores.Entities.IsAlive(id)); // authoritative destruction remains immediate — see EntityProtocol.SendDeath doc comment
        Assert.True(fx.Transport.Captured.Count > before); // death reaction + remove-actor reached the viewer
    }
}
