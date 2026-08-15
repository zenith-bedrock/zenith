using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Phase XIV — first passive-mob vertical slice: wander, no retaliation, melee-killable. ECS-authoritative since Phase XXII.</summary>
public sealed class CowSystemTests
{
    private static Position Pos(CowSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static HealthState Health(CowSystem system, EntityId id)
    {
        Assert.True(system.Stores.Health.TryGet(id, out var health));
        return health.State;
    }

    private static ulong BreedCooldown(CowSystem system, EntityId id)
    {
        Assert.True(system.CowStates.TryGet(id, out var state));
        return state.BreedCooldownUntilTick;
    }

    [Fact]
    public void Bootstrap_spawns_one_cow_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farmer");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Cows);
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void Cow_wanders_without_any_player_nearby()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-farmer");
        player.PositionX = 1000; // far away — a chase AI would never move a cow toward this
        player.PositionZ = 1000;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnCow(0, Blocks.FlatSpawnY, 0);

        var start = Pos(system, id);
        for (var i = 0; i < 60; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        var end = Pos(system, id);
        Assert.True(end.X != start.X || end.Z != start.Z);
    }

    [Fact]
    public void Cow_never_attacks_or_damages_a_nearby_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(2));
        system.SpawnCow(player.PositionX + 0.5f, player.PositionY, player.PositionZ);

        for (var i = 0; i < 100; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Health);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void Melee_kill_drops_beef_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("rancher");
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 3; i++)
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.Empty(system.Cows);
        Assert.False(system.Stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:beef"), loot.Id.Value);
        Assert.Equal(1, loot.Count);
        Assert.Equal(1, player.ExperiencePoints);
    }

    [Fact]
    public void Attack_outside_reach_does_not_mutate_cow_health()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnCow(player.PositionX + 10, player.PositionY, player.PositionZ);
        var maxHealth = Health(system, id).Maximum;

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(maxHealth, Health(system, id).Current);
        Assert.Single(system.Cows);
    }

    [Fact]
    public void LethalDamage_refuses_transition_when_loot_cannot_be_stored()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(i + 100, (int)player.PositionY, 0,
                StackId.FromBlock(Blocks.Dirt), 1, fx.Players.AllocateRuntimeId(), out _));

        var maxHealth = Health(system, id).Maximum;
        Assert.False(system.TryApplyDamage(id, DamageSource.Melee, maxHealth, fx.Players.Online, fx.Clock.CurrentTick));
        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.Equal(maxHealth, Health(system, id).Current);
        Assert.Equal(FloorDropStore.SoftCap, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void A_late_joining_player_is_replicated_the_already_active_cow()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = -1;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnCow(first.PositionX + 1, first.PositionY, first.PositionZ);
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
    public void Projectile_kill_also_awards_experience_via_the_shared_reward_path()
    {
        var fx = new IntentTestFixture();
        var shooter = fx.AddInGamePlayer("shooter");
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnCow(shooter.PositionX + 1, shooter.PositionY, shooter.PositionZ);

        Assert.True(system.TryApplyDamage(
            id, DamageSource.Projectile(shooter.RuntimeId), Health(system, id).Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Equal(1, shooter.ExperiencePoints);
    }

    [Fact]
    public void Two_cows_within_breed_radius_produce_a_calf_after_one_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farmer");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        system.SpawnCow(0, Blocks.FlatSpawnY, 0);
        system.SpawnCow(1, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.BirthCount);
        Assert.Equal(3, system.Cows.Count);
    }

    [Fact]
    public void A_cow_that_just_bred_cannot_breed_again_immediately()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farmer");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        system.SpawnCow(0, Blocks.FlatSpawnY, 0);
        system.SpawnCow(1, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.BirthCount);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.BirthCount); // still on cooldown — no second birth on the very next tick
    }

    [Fact]
    public void Breeding_stops_once_the_population_cap_is_reached()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farmer");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        for (var i = 0; i < 32; i++)
            system.SpawnCow(i * 0.01f, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.BirthCount);
        Assert.Equal(32, system.Cows.Count);
    }

    [Fact]
    public void A_cow_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-farmer");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnCow(0, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);
        Assert.True(system.Stores.Entities.IsAlive(id));

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Cows);
    }

    [Fact]
    public void A_cow_kept_within_range_of_a_player_never_despawns()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("nearby-farmer");
        player.PositionX = 30;
        player.PositionZ = 0;
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnCow(0, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks + 100);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.DespawnCount);
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void Feeding_wheat_clears_breed_cooldown_and_consumes_one_wheat()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("farmer");
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);
        ref var state = ref system.CowStates.GetRef(id);
        state.BreedCooldownUntilTick = 5_000;
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:wheat"), 1);
        player.SelectedHotbarSlot = 0;

        Assert.True(system.Stores.Identities.TryGet(id, out var identity));
        player.SubmitInteractIntent(identity.ActorUniqueId);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.FeedCount);
        Assert.Equal(0, player.Inventory.Get(0).Count);
        Assert.True(BreedCooldown(system, id) <= fx.Clock.CurrentTick);
    }

    [Fact]
    public void Interacting_without_wheat_does_not_feed()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("empty-handed-farmer");
        var system = new CowSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);
        ref var state = ref system.CowStates.GetRef(id);
        state.BreedCooldownUntilTick = 5_000;

        Assert.True(system.Stores.Identities.TryGet(id, out var identity));
        player.SubmitInteractIntent(identity.ActorUniqueId);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.FeedCount);
        Assert.Equal(5_000UL, BreedCooldown(system, id));
    }
}
