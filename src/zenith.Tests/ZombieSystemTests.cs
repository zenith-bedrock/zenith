using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

public sealed class ZombieSystemTests
{
    [Fact]
    public void BootstrapSpawnsOneConcreteZombieAndMovesTowardPlayer()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new ZombieSystem(fx.World, fx.Players, new ZombieStore());

        system.Tick(fx.Clock, fx.Players.Online);

        var zombie = Assert.Single(system.Zombies.Active);
        var initialDistance = MathF.Abs(zombie.PositionX - player.PositionX);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(zombie.IsActive);
        Assert.True(MathF.Abs(zombie.PositionX - player.PositionX) < initialDistance);
        Assert.NotEqual(0, zombie.EntityId);
        Assert.Equal((ulong)zombie.EntityId, zombie.RuntimeId);
    }

    [Fact]
    public void AttackIsValidatedByGameplayOwnerAndDeathRemovesExactlyOnce()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, zombie.Health.Current);

        for (var i = 0; i < 4; i++)
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Empty(store.Active);
        Assert.False(zombie.IsActive);
        Assert.True(zombie.Health.IsDead);
        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(store.Active);
        Assert.Equal(0f, zombie.Health.Current);
    }

    [Fact]
    public void AttackOutsideReachDoesNotMutateZombieHealth()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 10, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(zombie.Health.Maximum, zombie.Health.Current);
        Assert.Single(store.Active);
    }

    [Fact]
    public void ZombieInReach_damagesPlayerOncePerCooldown()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store);

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
        var system = new ZombieSystem(fx.World, fx.Players, new ZombieStore());
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
}
