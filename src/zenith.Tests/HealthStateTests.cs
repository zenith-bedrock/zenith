using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Leaf invariants for the reusable, packet-free health state.</summary>
public class HealthStateTests
{
    [Fact]
    public void Controlled_melee_damage_updates_health_without_entity_or_network_state()
    {
        var health = new HealthState(maximum: 20f);

        var result = health.Apply(DamageSource.Melee, amount: 6f, currentTick: 0);

        Assert.Equal(DamageResultKind.Applied, result.Kind);
        Assert.Equal(20f, result.PreviousHealth);
        Assert.Equal(14f, result.CurrentHealth);
        Assert.Equal(14f, health.Current);
        Assert.False(health.IsDead);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Invalid_damage_is_rejected_without_mutating_authoritative_state(float amount)
    {
        var health = new HealthState(maximum: 20f);

        var result = health.Apply(DamageSource.Generic, amount, currentTick: 0);

        Assert.Equal(DamageResultKind.Rejected, result.Kind);
        Assert.Equal(20f, health.Current);
        Assert.False(health.IsDead);
        Assert.Null(health.FatalSource);
    }

    [Fact]
    public void Lethal_transition_is_single_and_preserves_the_first_fatal_source()
    {
        var health = new HealthState(maximum: 5f);

        var fatal = health.Apply(DamageSource.Fall, amount: 5f, currentTick: 0);
        var replay = health.Apply(DamageSource.Melee, amount: 1f, currentTick: 0);

        Assert.Equal(DamageResultKind.Died, fatal.Kind);
        Assert.Equal(DamageResultKind.AlreadyDead, replay.Kind);
        Assert.True(health.IsDead);
        Assert.Equal(0f, health.Current);
        Assert.Equal(DamageSource.Fall, health.FatalSource);
        Assert.Equal("fall", health.FatalSource!.Value.DeathInfoCause);
    }

    [Fact]
    public void Respawn_reset_restores_full_health_and_opens_a_new_lifecycle()
    {
        var health = new HealthState(maximum: 12f);
        _ = health.Apply(DamageSource.Void, amount: 12f, currentTick: 0);

        health.RestoreFull();

        Assert.False(health.IsDead);
        Assert.Equal(12f, health.Current);
        Assert.Null(health.FatalSource);
        Assert.Equal(DamageResultKind.Applied, health.Apply(DamageSource.Generic, amount: 1f, currentTick: 0).Kind);
    }

    /// <summary>
    /// Phase XXIII-B real-client-review finding: Zenith had no hit-invulnerability at all (player or
    /// mob) — every damage source landed in full every tick, unlike vanilla's ~10-tick grace window
    /// after a hit. This locks in the window's three defining behaviors in one place, independent of
    /// any gameplay system.
    /// </summary>
    [Fact]
    public void A_second_equal_or_lesser_hit_inside_the_window_is_rejected()
    {
        var health = new HealthState(maximum: 20f);

        var first = health.Apply(DamageSource.Melee, amount: 5f, currentTick: 0);
        var secondEqual = health.Apply(DamageSource.Melee, amount: 5f, currentTick: 5);
        var secondLesser = health.Apply(DamageSource.Melee, amount: 3f, currentTick: 9);

        Assert.Equal(DamageResultKind.Applied, first.Kind);
        Assert.Equal(DamageResultKind.Rejected, secondEqual.Kind);
        Assert.Equal(DamageResultKind.Rejected, secondLesser.Kind);
        Assert.Equal(15f, health.Current);
    }

    /// <summary>
    /// Regression for ADR §98's second Adendo: an overriding hit must only apply its delta over the
    /// hit that opened the window (8 - 5 = 3), not the full 8 again on top of the already-applied 5 —
    /// otherwise the shared portion double-counts. 20 max - 5 - 3 = 12, matching
    /// Dragonfly/PocketMine/vanilla's "subtract only the delta" rule for this exact case.
    /// </summary>
    [Fact]
    public void A_strictly_harder_hit_inside_the_window_only_applies_the_delta()
    {
        var health = new HealthState(maximum: 20f);

        _ = health.Apply(DamageSource.Melee, amount: 5f, currentTick: 0);
        var harder = health.Apply(DamageSource.Melee, amount: 8f, currentTick: 5);

        Assert.Equal(DamageResultKind.Applied, harder.Kind);
        Assert.Equal(12f, health.Current);
    }

    [Fact]
    public void An_equal_hit_lands_again_once_the_window_has_elapsed()
    {
        var health = new HealthState(maximum: 20f);

        _ = health.Apply(DamageSource.Melee, amount: 5f, currentTick: 0);
        var afterWindow = health.Apply(DamageSource.Melee, amount: 5f, currentTick: 10);

        Assert.Equal(DamageResultKind.Applied, afterWindow.Kind);
        Assert.Equal(10f, health.Current);
    }

    /// <summary>Void bypasses the window — matches vanilla (falling out of the world damages every tick, not just once).</summary>
    [Fact]
    public void Void_damage_ignores_the_invulnerability_window()
    {
        var health = new HealthState(maximum: 20f);

        _ = health.Apply(DamageSource.Melee, amount: 5f, currentTick: 0);
        var void1 = health.Apply(DamageSource.Void, amount: 5f, currentTick: 1);
        var void2 = health.Apply(DamageSource.Void, amount: 5f, currentTick: 2);

        Assert.Equal(DamageResultKind.Applied, void1.Kind);
        Assert.Equal(DamageResultKind.Applied, void2.Kind);
        Assert.Equal(5f, health.Current);
    }
}
