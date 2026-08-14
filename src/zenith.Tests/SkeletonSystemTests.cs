using Zenith.Ecs;
using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public sealed class SkeletonSystemTests
{
    private static ProjectileSystem CreateProjectileSystem(IntentTestFixture fx, EntityRuntime stores)
    {
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var minecarts = new MinecartSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies.Owns, zombies.TryApplyDamage);
        damage.Register(minecarts.Owns, minecarts.TryApplyDamage);
        return new ProjectileSystem(fx.World, fx.Players, stores, damage);
    }

    private static HealthState Health(SkeletonSystem system, EntityId id)
    {
        Assert.True(system.Stores.Health.TryGet(id, out var health));
        return health.State;
    }

    [Fact]
    public void TargetInRange_spawnsProjectileOncePerRangedCooldown()
    {
        var fx = new IntentTestFixture();
        var target = fx.AddInGamePlayer("target");
        target.PositionY = 100f;
        var stores = new EntityRuntime();
        var projectileSystem = CreateProjectileSystem(fx, stores);
        var system = new SkeletonSystem(fx.World, fx.Players, stores, projectileSystem, fx.Context.ItemPalette);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(system.Skeletons);
        Assert.Single(projectileSystem.Projectiles);

        for (var i = 0; i < 29; i++)
        {
            fx.Clock.Advance();
            system.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.Single(projectileSystem.Projectiles);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, projectileSystem.Projectiles.Count);
    }

    [Fact]
    public void MeleeDeath_createsConcreteBoneLootExactlyOnce()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var stores = new EntityRuntime();
        var projectileSystem = CreateProjectileSystem(fx, stores);
        var system = new SkeletonSystem(fx.World, fx.Players, stores, projectileSystem, fx.Context.ItemPalette);
        var id = system.SpawnSkeleton(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 5; i++)
        {
            player.SubmitAttackIntent();
            fx.Clock.AdvanceBy(11);
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Empty(system.Skeletons);
        Assert.False(stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:bone"), loot.Id.Value);
        Assert.True(loot.Id.IsItem);
        Assert.Equal(1, loot.Count);

        // The item entity is placed at the skeleton cell; move the player into its pickup AABB.
        player.PositionX = loot.Pos.X + 0.5f;
        player.PositionY = loot.Pos.Y;
        player.PositionZ = loot.Pos.Z + 0.5f;
        Assert.True(FloorDropSystem.IsWithinPickupReach(player, loot.Pos.X, loot.Pos.Y, loot.Pos.Z));

        var pickup = new FloorDropSystem(fx.World);
        for (var i = 0; i < FloorDropStore.DefaultPickupDelay; i++)
            pickup.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(fx.World.FloorDrops.Snapshot());
        Assert.Contains(
            Enumerable.Range(0, PlayerInventory.FullInventorySize).Select(player.Inventory.Get),
            slot => slot.Id == loot.Id && slot.Count == 1);
    }

    /// <summary>Phase XXIII — Skeleton previously never moved after spawn; this proves the spacing mechanism, not tuned distances.</summary>
    [Fact]
    public void A_player_closing_inside_the_retreat_range_pushes_the_skeleton_back()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("closer");
        player.PositionY = Blocks.FlatSpawnY;
        var stores = new EntityRuntime();
        var projectileSystem = CreateProjectileSystem(fx, stores);
        var system = new SkeletonSystem(fx.World, fx.Players, stores, projectileSystem, fx.Context.ItemPalette);
        var id = system.SpawnSkeleton(player.PositionX + 3f, player.PositionY, player.PositionZ); // inside the 5-block retreat threshold

        Assert.True(system.Stores.Positions.TryGet(id, out var before));
        var distanceBefore = MathF.Abs(before.X - player.PositionX);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.Stores.Positions.TryGet(id, out var after));
        var distanceAfter = MathF.Abs(after.X - player.PositionX);
        Assert.True(distanceAfter > distanceBefore);
    }

    [Fact]
    public void A_distant_player_within_detection_but_beyond_the_approach_threshold_pulls_the_skeleton_closer()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("far");
        player.PositionY = Blocks.FlatSpawnY;
        var stores = new EntityRuntime();
        var projectileSystem = CreateProjectileSystem(fx, stores);
        var system = new SkeletonSystem(fx.World, fx.Players, stores, projectileSystem, fx.Context.ItemPalette);
        var id = system.SpawnSkeleton(player.PositionX + 15f, player.PositionY, player.PositionZ); // beyond the 10-block approach threshold, within 20-block detection

        Assert.True(system.Stores.Positions.TryGet(id, out var before));
        var distanceBefore = MathF.Abs(before.X - player.PositionX);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.Stores.Positions.TryGet(id, out var after));
        var distanceAfter = MathF.Abs(after.X - player.PositionX);
        Assert.True(distanceAfter < distanceBefore);
    }

    [Fact]
    public void A_player_within_the_preferred_band_is_held_at_range_not_approached_or_retreated_from()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("banded");
        player.PositionY = Blocks.FlatSpawnY;
        var stores = new EntityRuntime();
        var projectileSystem = CreateProjectileSystem(fx, stores);
        var system = new SkeletonSystem(fx.World, fx.Players, stores, projectileSystem, fx.Context.ItemPalette);
        var id = system.SpawnSkeleton(player.PositionX + 7f, player.PositionY, player.PositionZ); // between the 5-block retreat and 10-block approach thresholds

        Assert.True(system.Stores.Positions.TryGet(id, out var before));

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(system.Stores.Positions.TryGet(id, out var after));
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Z, after.Z, 3);
    }

    /// <summary>
    /// Phase XXIII-B real-client finding: "a bola de neve do esqueleto some antes de me atingir" —
    /// the shot previously launched with a flat +0.08 vertical velocity regardless of range, so
    /// against a constant per-tick gravity it fell several blocks short over any real shot distance
    /// and despawned into the ground well before reaching the target. This proves the ballistic-arc
    /// fix actually lands a hit at a real shot distance, not just that a projectile spawns.
    /// </summary>
    [Fact]
    public void A_ranged_shot_at_a_real_distance_actually_reaches_and_damages_the_target()
    {
        var fx = new IntentTestFixture();
        var target = fx.AddInGamePlayer("victim");
        target.PositionY = 100f;
        var stores = new EntityRuntime();
        var projectileSystem = CreateProjectileSystem(fx, stores);
        var system = new SkeletonSystem(fx.World, fx.Players, stores, projectileSystem, fx.Context.ItemPalette);
        system.SpawnSkeleton(target.PositionX + 10f, target.PositionY, target.PositionZ);

        var initialHealth = target.Health;

        for (var i = 0; i < 60; i++)
        {
            fx.Clock.Advance();
            system.Tick(fx.Clock, fx.Players.Online);
            projectileSystem.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(target.Health < initialHealth);
    }
}
