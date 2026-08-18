using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>
/// Phase XV — Enderman: teleport movement and damage-triggered aggro, sharing only kill bookkeeping.
/// ECS-authoritative since the Creeper/Enderman/Golem/Fish migration (docs/decisions.md).
/// </summary>
public sealed class EndermanSystemTests
{
    private static Position Pos(EndermanSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static HealthState Health(EndermanSystem system, EntityId id)
    {
        Assert.True(system.Stores.Health.TryGet(id, out var health));
        return health.State;
    }

    [Fact]
    public void Bootstrap_spawns_one_enderman_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("bootstrapper");
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Endermen);
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void Passive_enderman_eventually_teleports_without_ever_being_hit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-observer");
        player.PositionX = 500; // far outside any melee/aggro range
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(7));
        var id = system.SpawnEnderman(0, Blocks.FlatSpawnY, 0);

        var start = Pos(system, id);
        for (var i = 0; i < 150; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
        var end = Pos(system, id);

        Assert.True(system.TeleportCount > 0);
        Assert.True(end.X != start.X || end.Z != start.Z);
        Assert.True(system.EndermanStates.TryGet(id, out var state));
        Assert.Null(state.AggroTargetRuntimeId);
    }

    [Fact]
    public void A_landed_player_hit_provokes_the_enderman()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("provoker");
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnEnderman(player.PositionX + 1, player.PositionY, player.PositionZ);
        var maxHealth = Health(system, id).Maximum;

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.EndermanStates.TryGet(id, out var state));
        Assert.Equal(player.RuntimeId, state.AggroTargetRuntimeId);
        Assert.True(state.AggroTicksRemaining > 0);
        Assert.True(Health(system, id).Current < maxHealth);
    }

    /// <summary>Phase XXVI — closes the entity-fidelity finding that Enderman never set Yaw at all.</summary>
    [Fact]
    public void Aggro_tick_orients_the_enderman_toward_its_target()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("provoker");
        player.PositionX = 10;
        player.PositionZ = 0;
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnEnderman(player.PositionX + 1, player.PositionY, player.PositionZ);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        var pos = Pos(system, id);
        var expectedYaw = LookMath.YawTowards(player.PositionX - pos.X, player.PositionZ - pos.Z);
        Assert.Equal(expectedYaw, pos.Yaw, precision: 3);
    }

    [Fact]
    public void Aggravated_enderman_teleports_toward_and_eventually_retaliates_against_its_attacker()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("retaliation-target");
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(3));
        // Spawned out of melee range so it must close the distance via teleport before it can hit back.
        var id = system.SpawnEnderman(player.PositionX + 20, player.PositionY, player.PositionZ);

        // Provoke directly (same effect as a landed hit) so this test isolates aggro *movement/retaliation*
        // rather than re-testing the "a hit provokes it" path already covered above.
        ref var state = ref system.EndermanStates.GetRef(id);
        state.AggroTargetRuntimeId = player.RuntimeId;
        state.AggroTicksRemaining = 100;

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
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnEnderman(player.PositionX + 30, player.PositionY, player.PositionZ);
        ref var state = ref system.EndermanStates.GetRef(id);
        state.AggroTargetRuntimeId = player.RuntimeId;
        state.AggroTicksRemaining = 1;

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.EndermanStates.TryGet(id, out var after));
        Assert.Equal(0, after.AggroTicksRemaining);
        Assert.Null(after.AggroTargetRuntimeId);
    }

    [Fact]
    public void Melee_kill_drops_an_ender_pearl_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("slayer");
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnEnderman(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 3; i++)
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Endermen);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:ender_pearl"), loot.Id.Value);
        Assert.Equal(5, player.ExperiencePoints);
    }

    [Fact]
    public void Attack_outside_reach_does_not_mutate_enderman_health()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnEnderman(player.PositionX + 10, player.PositionY, player.PositionZ);
        var maxHealth = Health(system, id).Maximum;

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(maxHealth, Health(system, id).Current);
        Assert.Single(system.Endermen);
    }

    [Fact]
    public void A_late_joining_player_is_replicated_the_already_active_enderman()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = -1;
        var system = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnEnderman(first.PositionX + 5, first.PositionY, first.PositionZ);
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
    public void Arrow_can_damage_an_enderman_through_the_shared_dispatch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var stores = new EntityRuntime();
        var endermen = new EndermanSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(endermen.Owns, endermen.TryApplyDamage);
        var projectiles = new ProjectileSystem(fx.World, fx.Players, stores, damage, new PlayerSpatialIndex());
        var id = endermen.SpawnEnderman(10f, 100f, 0f); // 20 HP; a 4-damage hit must leave it alive but wounded, not untouched.
        var maxHealth = Health(endermen, id).Maximum;

        projectiles.TrySpawnFromActor(owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f, fx.Players.Online);
        projectiles.Tick(fx.Clock, fx.Players.Online);

        Assert.True(Health(endermen, id).Current < maxHealth); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(projectiles.Projectiles);
    }
}
