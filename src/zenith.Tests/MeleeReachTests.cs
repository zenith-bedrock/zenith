using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Geometry;
using Xunit;

namespace Zenith.Tests;

public class MeleeReachTests
{
    [Theory]
    [InlineData(3.4f, 0f, true)]
    [InlineData(4f, 0f, false)]
    [InlineData(1f, 10f, false)]
    public void Player_melee_uses_eye_distance_to_mob_bounds(float x, float relativeY, bool accepted)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("reach");
        var health = new HealthState(20);
        var view = new DamageableActorView(99, 99, x, player.PositionY + relativeY, 0, health);
        player.Intents.SubmitAttackIntent(99);
        var result = DamageableActorCombatCore.TryApplyPlayerMeleeAttack(view, fx.Players.Online,
            MobDefinitions.Cow, fx.Context.ItemPalette, 0, (source, amount, _, tick) => health.Apply(source, amount, tick).WasApplied);
        Assert.Equal(accepted, result); Assert.Equal(accepted ? 19f : 20f, health.Current);
        Assert.False(player.Intents.TryConsumeAttackIntent());
    }

    [Fact]
    public void Out_of_reach_click_cannot_land_when_target_later_moves_closer()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("stale-click");
        var health = new HealthState(20); player.Intents.SubmitAttackIntent(99);
        foreach (var x in new[] { 5f, 1f })
        {
            var view = new DamageableActorView(99, 99, x, player.PositionY, 0, health);
            Assert.False(DamageableActorCombatCore.TryApplyPlayerMeleeAttack(view, fx.Players.Online,
                MobDefinitions.Cow, fx.Context.ItemPalette, 0, (source, amount, _, tick) => health.Apply(source, amount, tick).WasApplied));
        }
        Assert.Equal(20f, health.Current);
    }

    [Theory]
    [InlineData(4f, true)]
    [InlineData(7.4f, true)]
    [InlineData(8f, false)]
    public void Creative_actor_reach_is_distinct_from_survival(float x, bool accepted)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("creative-reach");
        player.SetGameMode(Zenith.Player.GameMode.Creative);
        var health = new HealthState(20);
        var view = new DamageableActorView(99, 99, x, player.PositionY, 0, health);
        player.Intents.SubmitAttackIntent(99);
        Assert.Equal(accepted, DamageableActorCombatCore.TryApplyPlayerMeleeAttack(view, fx.Players.Online,
            MobDefinitions.Cow, fx.Context.ItemPalette, 0, (source, amount, _, tick) => health.Apply(source, amount, tick).WasApplied));
        Assert.Equal(accepted ? 19f : 20f, health.Current);
    }

    [Fact]
    public void Distance_to_asymmetric_box_is_zero_inside_and_uses_nearest_surface_outside()
    {
        var box = Aabb.FromMinMax(-2, 4, 10, 3, 8, 12);
        Assert.Equal(0f, box.DistanceSquaredTo(0, 5, 11));
        Assert.Equal(14f, box.DistanceSquaredTo(4, 10, 15));
    }
}
