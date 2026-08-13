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
            fx.Clock.Advance();
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
}
