using Zenith.Gameplay;
using Xunit;

namespace Zenith.Tests;

/// <summary>Leaf invariants for the reusable, packet-free health state.</summary>
public class HealthStateTests
{
    [Fact]
    public void Controlled_melee_damage_updates_health_without_entity_or_network_state()
    {
        var health = new HealthState(maximum: 20f);

        var result = health.Apply(DamageSource.Melee, amount: 6f);

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

        var result = health.Apply(DamageSource.Generic, amount);

        Assert.Equal(DamageResultKind.Rejected, result.Kind);
        Assert.Equal(20f, health.Current);
        Assert.False(health.IsDead);
        Assert.Null(health.FatalSource);
    }

    [Fact]
    public void Lethal_transition_is_single_and_preserves_the_first_fatal_source()
    {
        var health = new HealthState(maximum: 5f);

        var fatal = health.Apply(DamageSource.Fall, amount: 5f);
        var replay = health.Apply(DamageSource.Melee, amount: 1f);

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
        _ = health.Apply(DamageSource.Void, amount: 12f);

        health.RestoreFull();

        Assert.False(health.IsDead);
        Assert.Equal(12f, health.Current);
        Assert.Null(health.FatalSource);
        Assert.Equal(DamageResultKind.Applied, health.Apply(DamageSource.Generic, amount: 1f).Kind);
    }
}
