using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public sealed class SkeletonSystemTests
{
    [Fact]
    public void TargetInRange_spawnsProjectileOncePerRangedCooldown()
    {
        var fx = new IntentTestFixture();
        var target = fx.AddInGamePlayer("target");
        target.PositionY = 100f;
        var zombies = new ZombieStore();
        var projectiles = new ProjectileStore();
        var projectileSystem = new ProjectileSystem(fx.World, fx.Players, projectiles, new ZombieSystem(fx.World, fx.Players, zombies, fx.Context.ItemPalette));
        var skeletons = new SkeletonStore();
        var system = new SkeletonSystem(fx.World, fx.Players, skeletons, projectileSystem, fx.Context.ItemPalette);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(skeletons.Active);
        Assert.Single(projectiles.Active);

        for (var i = 0; i < 29; i++)
        {
            fx.Clock.Advance();
            system.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.Single(projectiles.Active);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, projectiles.Active.Count);
    }

    [Fact]
    public void MeleeDeath_createsConcreteBoneLootExactlyOnce()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var zombies = new ZombieStore();
        var projectiles = new ProjectileStore();
        var projectileSystem = new ProjectileSystem(fx.World, fx.Players, projectiles,
            new ZombieSystem(fx.World, fx.Players, zombies, fx.Context.ItemPalette));
        var skeletons = new SkeletonStore();
        var skeleton = new Skeleton(fx.Players.AllocateRuntimeId(), 77, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(skeletons.TryAdd(skeleton));
        var system = new SkeletonSystem(fx.World, fx.Players, skeletons, projectileSystem, fx.Context.ItemPalette);

        for (var i = 0; i < 5; i++)
        {
            player.SubmitAttackIntent();
            fx.Clock.Advance();
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Empty(skeletons.Active);
        Assert.True(skeleton.Health.IsDead);
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
