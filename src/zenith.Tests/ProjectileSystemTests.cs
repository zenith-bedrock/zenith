using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

public sealed class ProjectileSystemTests
{
    [Fact]
    public void ProjectileSpawnIsTickOwnedAndCarriesItsOwnerIdentity()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("owner");
        owner.Yaw = -90f; // +X
        var system = CreateSystem(fx, out _, out _);

        owner.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        var projectile = Assert.Single(system.Projectiles.Active);
        Assert.Equal(owner.RuntimeId, projectile.OwnerRuntimeId);
        Assert.Equal((ulong)projectile.EntityId, projectile.RuntimeId);
        Assert.True(projectile.PositionX > owner.PositionX);
    }

    [Fact]
    public void ProjectileImpactDamagesZombieWithProjectileAttributionAndRemovesOnce()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("owner");
        owner.Yaw = -90f;
        var system = CreateSystem(fx, out var zombies, out _);
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, 1f, owner.PositionY, owner.PositionZ);
        Assert.True(zombies.TryAdd(zombie));
        zombie.ApplyDamage(DamageSource.Generic, 14f);

        owner.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(system.Projectiles.Active);
        Assert.False(zombie.IsActive);
        Assert.True(zombie.Health.IsDead);
        Assert.Equal(DamageCause.Projectile, zombie.Health.FatalSource?.Cause);
        Assert.Equal(owner.RuntimeId, zombie.Health.FatalSource?.OwnerRuntimeId);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(system.Projectiles.Active);
        Assert.Equal(0f, zombie.Health.Current);
    }

    [Fact]
    public void WorldImpactRemovesProjectileAndNeverMutatesZombieState()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("observer");
        var system = CreateSystem(fx, out var zombies, out var projectiles);
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, 20f, player.PositionY, 0f);
        Assert.True(zombies.TryAdd(zombie));
        var projectile = new Projectile(
            fx.Players.AllocateRuntimeId(), 101, player.RuntimeId,
            0f, -60.5f, 0f, 0f, -0.1f, 0f);
        Assert.True(projectiles.TryAdd(projectile));

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(projectiles.Active);
        Assert.False(projectile.IsActive);
        Assert.Equal(zombie.Health.Maximum, zombie.Health.Current);
    }

    [Fact]
    public void LateJoinReceivesExistingProjectileState()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Yaw = -90f;
        var system = CreateSystem(fx, out _, out _);
        first.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(system.Projectiles.Active);
        var before = fx.Transport.Captured.Count;

        _ = fx.AddInGamePlayer("late");
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var player in fx.Players.Online)
            player.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
    }

    private static ProjectileSystem CreateSystem(IntentTestFixture fx, out ZombieStore zombies, out ProjectileStore projectiles)
    {
        zombies = new ZombieStore();
        projectiles = new ProjectileStore();
        return new ProjectileSystem(fx.World, fx.Players, projectiles, new ZombieSystem(fx.World, fx.Players, zombies));
    }
}
