using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
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
        var projectileSystem = new ProjectileSystem(fx.World, fx.Players, projectiles, new ZombieSystem(fx.World, fx.Players, zombies));
        var skeletons = new SkeletonStore();
        var system = new SkeletonSystem(fx.Players, skeletons, projectileSystem);

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
}
