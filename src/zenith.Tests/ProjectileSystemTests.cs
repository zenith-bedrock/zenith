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
    public void ActorProjectileImpactDamagesAnotherPlayerAndRemovesOnce()
    {
        var fx = new IntentTestFixture();
        var target = fx.AddInGamePlayer("target");
        var system = CreateSystem(fx, out _, out var projectiles);
        var projectile = new Projectile(
            fx.Players.AllocateRuntimeId(), 102, 999,
            0.4f, target.PositionY + 1f, target.PositionZ, -0.1f, 0f, 0f);
        Assert.True(projectiles.TryAdd(projectile));

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(projectiles.Active);
        Assert.Equal(14f, target.Health);
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
    public void Projectile_skips_projection_when_authoritative_position_is_unchanged()
    {
        var fx = new IntentTestFixture();
        var observer = fx.AddInGamePlayer("observer");
        observer.Chunks.Radius = -1;
        var system = CreateSystem(fx, out _, out var projectiles);
        var projectile = new Projectile(
            fx.Players.AllocateRuntimeId(), 101, 999,
            0.4f, -57f, 0f, 0f, 0f, 0f);
        Assert.True(projectiles.TryAdd(projectile));

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.ReplicatedMoveCount);
        Assert.True(system.ReplicatedMoveSkippedCount > 0);
        Assert.True(projectile.IsActive);
    }

    [Fact]
    public void LateJoinReceivesExistingProjectileState()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.PositionY = 100f;
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        first.Yaw = -90f;
        var system = CreateSystem(fx, out _, out _);
        first.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(system.Projectiles.Active);
        var before = fx.Transport.Captured.Count;

        var late = fx.AddInGamePlayer("late");
        late.Chunks.Radius = 1;
        late.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var player in fx.Players.Online)
            player.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
    }

    [Fact]
    public void Interest_reconciles_late_join_and_reconnect_without_stale_actor_state()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = -1;
        var system = CreateSystem(fx, out _, out var projectiles);
        var projectile = new Projectile(
            fx.Players.AllocateRuntimeId(), 401, first.RuntimeId,
            0.5f, -57f, 0.5f, 0f, 0f, 0f);
        Assert.True(projectiles.TryAdd(projectile));

        system.Tick(fx.Clock, fx.Players.Online);
        var late = fx.AddInGamePlayer("late");
        late.Chunks.Radius = -1;
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, system.ReplicatedSpawnCount);

        late.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedRemovalCount);

        late.IsInGame = true;
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(3, system.ReplicatedSpawnCount);
        Assert.True(projectile.IsActive);
    }

    [Fact]
    public void ChunkInterest_reconcilesSpawnAndRemovalForRelevantObserversOnly()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("owner");
        var distant = fx.AddInGamePlayer("distant");
        owner.PositionY = distant.PositionY = 100f;
        owner.PositionZ = 0.5f;
        distant.PositionX = distant.PositionZ = 128f;
        owner.Chunks.Radius = distant.Chunks.Radius = 1;
        owner.Chunks.RememberMany([(0, 0)]);
        distant.Chunks.RememberMany([(8, 8)]);
        owner.Yaw = -90f;
        var system = CreateSystem(fx, out _, out _);

        owner.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        distant.Chunks.RememberMany([(0, 0)]);
        Assert.Single(system.Projectiles.Active);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(system.Projectiles.Active);
        Assert.Equal(2, system.ReplicatedSpawnCount);

        distant.Chunks.Forget(0, 0);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedRemovalCount);
    }

    [Fact]
    public void ChunkInterest_reconcilesWhenProjectileCrossesIntoAnotherKnownColumn()
    {
        var fx = new IntentTestFixture();
        var west = fx.AddInGamePlayer("west");
        var east = fx.AddInGamePlayer("east");
        west.Chunks.Radius = east.Chunks.Radius = 1;
        west.Chunks.RememberMany([(0, 0)]);
        east.Chunks.RememberMany([(1, 0)]);
        var system = CreateSystem(fx, out _, out var projectiles);
        var projectile = new Projectile(
            fx.Players.AllocateRuntimeId(), 301, west.RuntimeId,
            15.8f, 100f, 0.5f, 0.5f, 0f, 0f);
        Assert.True(projectiles.TryAdd(projectile));

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, system.ReplicatedSpawnCount);
        Assert.Equal(1, system.ReplicatedRemovalCount);
        Assert.Equal(0, system.ReplicatedMoveCount);
    }

    private static ProjectileSystem CreateSystem(IntentTestFixture fx, out ZombieStore zombies, out ProjectileStore projectiles)
    {
        zombies = new ZombieStore();
        projectiles = new ProjectileStore();
        return new ProjectileSystem(fx.World, fx.Players, projectiles, new ZombieSystem(fx.World, fx.Players, zombies, fx.Context.ItemPalette));
    }
}
