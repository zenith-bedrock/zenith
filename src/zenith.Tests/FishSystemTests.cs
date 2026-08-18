using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>
/// Phase XX, Priority 2 — second non-ground-navigation mob: 3D wander like Bat's, but validity
/// requires the destination cell to BE water, a third distinct movement rule. See FishSystem's
/// doc comment and docs/history/phases/phase-xx-runtime-relationships-findings.md.
/// ECS-authoritative since the Creeper/Enderman/Golem/Fish migration (docs/decisions.md).
/// </summary>
public sealed class FishSystemTests
{
    private static Position Pos(FishSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static HealthState Health(FishSystem system, EntityId id)
    {
        Assert.True(system.Stores.Health.TryGet(id, out var health));
        return health.State;
    }

    [Fact]
    public void Bootstrap_spawns_one_fish_where_water_exists_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("angler");
        player.PositionX = 0;
        player.PositionY = Blocks.FlatSpawnY;
        player.PositionZ = 0;
        fx.World.SetBlock(4, (int)player.PositionY, 0, Blocks.Water);
        var system = new FishSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Fish);
        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.True(system.Stores.Identities.TryGet(id, out var identity));
        Assert.NotEqual(0, identity.ActorUniqueId);
        Assert.Equal((ulong)identity.ActorUniqueId, identity.ActorRuntimeId);
    }

    [Fact]
    public void Bootstrap_does_not_spawn_a_fish_when_no_water_exists()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dry-angler");
        player.PositionX = 0;
        player.PositionY = Blocks.FlatSpawnY;
        player.PositionZ = 0;
        var system = new FishSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(system.Fish);
    }

    [Fact]
    public void Fish_wanders_within_water_but_never_leaves_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-angler");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var world = fx.World;
        var y = Blocks.FlatSpawnY;
        for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
                for (var dy = -1; dy <= 1; dy++)
                    world.SetBlock(x, y + dy, z, Blocks.Water);
        var system = new FishSystem(world, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnFish(0, y, 0);

        var start = Pos(system, id);
        for (var i = 0; i < 200; i++)
        {
            system.Tick(fx.Clock, fx.Players.Online);
            var pos = Pos(system, id);
            Assert.Equal(Blocks.Water, world.GetBlock((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z)));
        }

        var end = Pos(system, id);
        Assert.True(end.X != start.X || end.Y != start.Y || end.Z != start.Z);
    }

    /// <summary>
    /// Phase XXIX regression: FishSystem never calls <see cref="GroundMobMovement"/> — it stays on
    /// its own water-occupancy rule (<c>TrySwim</c>) — so the ground-locomotion resolver and
    /// gravity model must have zero effect on fish movement.
    /// </summary>
    [Fact]
    public void Fish_water_behavior_is_not_broken()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("angler");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var world = fx.World;
        var y = Blocks.FlatSpawnY;
        for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
                for (var dy = -1; dy <= 1; dy++)
                    world.SetBlock(x, y + dy, z, Blocks.Water);
        var system = new FishSystem(world, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(3));
        var id = system.SpawnFish(0, y, 0);

        for (var i = 0; i < 100; i++)
        {
            system.Tick(fx.Clock, fx.Players.Online);
            var pos = Pos(system, id);
            Assert.Equal(Blocks.Water, world.GetBlock((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z)));
        }
    }

    [Fact]
    public void Fish_never_attacks_or_damages_a_nearby_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        fx.World.SetBlock(
            (int)MathF.Floor(player.PositionX), (int)player.PositionY, (int)MathF.Floor(player.PositionZ), Blocks.Water);
        var system = new FishSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(2));
        system.SpawnFish(player.PositionX + 0.5f, player.PositionY, player.PositionZ);

        for (var i = 0; i < 100; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Health);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void Melee_kill_drops_cod_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fisher");
        var system = new FishSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnFish(player.PositionX + 1, player.PositionY, player.PositionZ);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online); // 3 health / 4 damage-per-hit — one hit kills

        Assert.Empty(system.Fish);
        Assert.False(system.Stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:cod"), loot.Id.Value);
        Assert.Equal(1, player.ExperiencePoints);
    }

    [Fact]
    public void A_fish_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-angler");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new FishSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnFish(0, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Fish);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingFishReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new FishSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnFish(0, Blocks.FlatSpawnY, 0);
        system.Tick(fx.Clock, fx.Players.Online);
        var before = fx.Transport.Captured.Count;

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
        Assert.True(first.IsInGame && second.IsInGame);
    }

    [Fact]
    public void Arrow_can_damage_a_fish_through_the_shared_dispatch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var stores = new EntityRuntime();
        var fish = new FishSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(fish.Owns, fish.TryApplyDamage);
        var projectiles = new ProjectileSystem(fx.World, fx.Players, stores, damage, new PlayerSpatialIndex());
        var id = fish.SpawnFish(10f, 100f, 0f); // 3 HP; a projectile hit outright kills a fish, unlike the tougher cow/spider.

        projectiles.TrySpawnFromActor(owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f, fx.Players.Online);
        projectiles.Tick(fx.Clock, fx.Players.Online);

        Assert.False(stores.Entities.IsAlive(id)); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(projectiles.Projectiles);
    }
}
