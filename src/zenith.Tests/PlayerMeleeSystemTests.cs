using Zenith.Gameplay.Runtime;
using Xunit;
using Zenith.Gameplay.Entities;

namespace Zenith.Tests;

public sealed class PlayerMeleeSystemTests
{
    [Fact]
    public void Explicit_runtime_target_damages_only_that_nearby_player()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var target = fx.AddInGamePlayer("target");
        var bystander = fx.AddInGamePlayer("bystander");
        target.PositionX = attacker.PositionX + 1;
        target.PositionZ = attacker.PositionZ;
        bystander.PositionX = attacker.PositionX + 1.5f;
        bystander.PositionZ = attacker.PositionZ;

        attacker.SubmitAttackIntent(target.RuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(16f, target.Health);
        Assert.Equal(bystander.MaxHealth, bystander.Health);
    }

    [Fact]
    public void Explicit_target_outside_reach_is_consumed_without_damage()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var target = fx.AddInGamePlayer("distant-target");
        target.PositionX = attacker.PositionX + 10;

        attacker.SubmitAttackIntent(target.RuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(target.MaxHealth, target.Health);
        Assert.False(attacker.TryConsumeAttackIntent());
    }
}
