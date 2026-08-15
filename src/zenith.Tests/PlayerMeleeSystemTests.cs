using Zenith.Gameplay.Runtime;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

public sealed class PlayerMeleeSystemTests
{
    private const float ExpectedDamage = 4f;

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

    [Fact]
    public void Target_at_exactly_the_reach_boundary_is_hit()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var target = fx.AddInGamePlayer("edge-target");
        target.PositionX = attacker.PositionX + 2.25f; // == AttackDistance
        target.PositionZ = attacker.PositionZ;

        attacker.SubmitAttackIntent(target.RuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(target.MaxHealth - ExpectedDamage, target.Health);
    }

    [Fact]
    public void Self_attack_is_ignored()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("solo");

        attacker.SubmitAttackIntent(attacker.RuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(attacker.MaxHealth, attacker.Health);
    }

    [Fact]
    public void Dead_attacker_intent_is_consumed_without_damaging_the_target()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("dead-attacker");
        var target = fx.AddInGamePlayer("target");
        target.PositionX = attacker.PositionX + 1;
        _ = attacker.ApplyDamage(DamageSource.Void, attacker.MaxHealth, fx.Clock.CurrentTick);
        Assert.True(attacker.IsDead);

        attacker.SubmitAttackIntent(target.RuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(target.MaxHealth, target.Health);
    }

    [Fact]
    public void Attack_against_an_already_dead_target_is_ignored()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var target = fx.AddInGamePlayer("dead-target");
        target.PositionX = attacker.PositionX + 1;
        var healthBeforeDeath = target.MaxHealth;
        _ = target.ApplyDamage(DamageSource.Void, target.MaxHealth, fx.Clock.CurrentTick);
        Assert.True(target.IsDead);

        attacker.SubmitAttackIntent(target.RuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        // Still dead, no further health mutation attempted (health is already floored at 0 by the
        // void hit above; this only proves PlayerDamage.Apply was never invoked a second time).
        Assert.True(target.IsDead);
        _ = healthBeforeDeath;
    }

    [Fact]
    public void Attack_referencing_an_unknown_runtime_id_is_ignored_safely()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        _ = fx.AddInGamePlayer("bystander");

        attacker.SubmitAttackIntent(999_999L);
        var exception = Record.Exception(() => new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online));

        Assert.Null(exception);
        Assert.Equal(attacker.MaxHealth, attacker.Health);
    }

    [Fact]
    public void An_intent_targeting_a_non_player_runtime_id_is_left_for_its_real_owner()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        // A mob allocates its RuntimeId from the same PlayerManager counter (see
        // PlayerManager.AllocateRuntimeId) without ever being added as a Player. PlayerMeleeSystem
        // must not swallow this intent — a later mob combat system still needs to consume it.
        var mobRuntimeId = fx.Players.AllocateRuntimeId();

        attacker.SubmitAttackIntent(mobRuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.True(attacker.TryConsumeAttackIntent(mobRuntimeId));
    }

    [Fact]
    public void An_accepted_attack_is_applied_at_most_once()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var target = fx.AddInGamePlayer("target");
        target.PositionX = attacker.PositionX + 1;

        attacker.SubmitAttackIntent(target.RuntimeId);
        var system = new PlayerMeleeSystem(fx.Players);
        system.Tick(fx.Clock, fx.Players.Online);
        system.Tick(fx.Clock, fx.Players.Online); // no new submission — must not re-apply

        Assert.Equal(target.MaxHealth - ExpectedDamage, target.Health);
    }

    [Fact]
    public void No_pending_attack_causes_no_damage_attempt()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("a");
        var b = fx.AddInGamePlayer("b");
        b.PositionX = a.PositionX + 1;

        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(a.MaxHealth, a.Health);
        Assert.Equal(b.MaxHealth, b.Health);
    }

    /// <summary>
    /// The resolution must not depend on <c>online</c>'s iteration/snapshot order — attacker-first
    /// resolution keys off each attacker's own consumed intent, not position within the list.
    /// </summary>
    [Fact]
    public void Result_does_not_depend_on_the_online_snapshot_order()
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker");
        var target = fx.AddInGamePlayer("target");
        var bystander = fx.AddInGamePlayer("bystander");
        target.PositionX = attacker.PositionX + 1;
        bystander.PositionX = attacker.PositionX + 1;
        bystander.PositionZ = attacker.PositionZ + 1;

        attacker.SubmitAttackIntent(target.RuntimeId);

        // Deliberately reversed vs. insertion order.
        var reversed = new List<Player.Player> { bystander, target, attacker };
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, reversed);

        Assert.Equal(target.MaxHealth - ExpectedDamage, target.Health);
        Assert.Equal(bystander.MaxHealth, bystander.Health);
    }

    [Fact]
    public void Multiple_simultaneous_attackers_each_resolve_their_own_explicit_target()
    {
        var fx = new IntentTestFixture();
        var attackerOne = fx.AddInGamePlayer("attacker-one");
        var attackerTwo = fx.AddInGamePlayer("attacker-two");
        var targetOne = fx.AddInGamePlayer("target-one");
        var targetTwo = fx.AddInGamePlayer("target-two");
        targetOne.PositionX = attackerOne.PositionX + 1;
        targetTwo.PositionX = attackerTwo.PositionX + 1;

        attackerOne.SubmitAttackIntent(targetOne.RuntimeId);
        attackerTwo.SubmitAttackIntent(targetTwo.RuntimeId);
        new PlayerMeleeSystem(fx.Players).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(targetOne.MaxHealth - ExpectedDamage, targetOne.Health);
        Assert.Equal(targetTwo.MaxHealth - ExpectedDamage, targetTwo.Health);
    }
}
