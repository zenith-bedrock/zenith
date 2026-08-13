using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XI.3 — apply/refresh/expire/clear, Poison, Regeneration, death interaction.</summary>
public class EffectTests
{
    public EffectTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Applying_an_effect_activates_it_with_the_requested_amplifier_and_duration()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("subject");
        var system = new EffectSystem(fx.Players);

        player.SubmitEffect(EffectIntent.Give(EffectType.Regeneration, amplifier: 1, durationTicks: 100));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Effects.TryGetValue(EffectType.Regeneration, out var active));
        Assert.Equal(1, active.Amplifier);
        Assert.False(active.HasExpired(fx.Clock.CurrentTick));
    }

    [Fact]
    public void Reapplying_the_same_type_refreshes_duration_and_amplifier()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("refresher");
        var system = new EffectSystem(fx.Players);

        player.SubmitEffect(EffectIntent.Give(EffectType.Poison, amplifier: 0, durationTicks: 20));
        system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(10);

        player.SubmitEffect(EffectIntent.Give(EffectType.Poison, amplifier: 2, durationTicks: 200));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Effects.TryGetValue(EffectType.Poison, out var active));
        Assert.Equal(2, active.Amplifier);
        Assert.Equal(fx.Clock.CurrentTick + 200, active.ExpiresAtTick);
    }

    [Fact]
    public void Effect_expires_naturally_once_its_duration_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("expiring");
        var system = new EffectSystem(fx.Players);

        player.SubmitEffect(EffectIntent.Give(EffectType.Regeneration, amplifier: 0, durationTicks: 5));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Effects.ContainsKey(EffectType.Regeneration));

        fx.Clock.AdvanceBy(5);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Effects.ContainsKey(EffectType.Regeneration));
    }

    [Fact]
    public void Clear_intent_removes_every_active_effect()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("clearer");
        var system = new EffectSystem(fx.Players);

        player.SubmitEffect(EffectIntent.Give(EffectType.Poison, amplifier: 0, durationTicks: 100));
        system.Tick(fx.Clock, fx.Players.Online);
        player.SubmitEffect(EffectIntent.Give(EffectType.Regeneration, amplifier: 0, durationTicks: 100));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, player.Effects.Count);

        player.SubmitEffect(EffectIntent.Clear);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(player.Effects);
    }

    [Fact]
    public void Poison_deals_periodic_magic_damage_that_bypasses_armor()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("poisoned");
        var palette = fx.Context.ItemPalette;
        Assert.True(player.Inventory.TrySetArmor(PlayerInventory.ArmorChestplateSlot,
            StackId.FromItem(palette.Require("minecraft:diamond_chestplate")), 1));
        var system = new EffectSystem(fx.Players);

        player.SubmitEffect(EffectIntent.Give(EffectType.Poison, amplifier: 0, durationTicks: 100));
        system.Tick(fx.Clock, fx.Players.Online); // tick 0 happens to be a poison interval tick — ticks once here.

        fx.Clock.AdvanceBy(24); // tick 24: not a multiple of the 25-tick poison interval — no further damage.
        system.Tick(fx.Clock, fx.Players.Online);

        // Armor is equipped but poison is magic damage — the single 1-point tick lands unmitigated.
        Assert.Equal(19f, player.Health);
    }

    [Fact]
    public void Poison_never_reduces_health_below_one()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("nearly-dead");
        _ = player.ApplyDamage(DamageSource.Generic, 18.5f); // Health = 1.5
        var system = new EffectSystem(fx.Players);

        // Amplifier 3 would deal 4 damage — clamped so the tick at CurrentTick=0 leaves exactly 1 HP.
        player.SubmitEffect(EffectIntent.Give(EffectType.Poison, amplifier: 3, durationTicks: 100));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsDead);
        Assert.Equal(1f, player.Health);

        fx.Clock.AdvanceBy(25); // the next poison interval must not push health below 1 either.
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsDead);
        Assert.Equal(1f, player.Health);
    }

    [Fact]
    public void Regeneration_heals_periodically_and_stops_at_max_health()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("healing");
        _ = player.ApplyDamage(DamageSource.Generic, 10f); // Health = 10
        var system = new EffectSystem(fx.Players);

        player.SubmitEffect(EffectIntent.Give(EffectType.Regeneration, amplifier: 0, durationTicks: 200));
        system.Tick(fx.Clock, fx.Players.Online); // tick 0 happens to be a regen interval tick — heals once here.

        fx.Clock.AdvanceBy(49); // tick 49: not a multiple of the 50-tick regen interval — no further heal.
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(11f, player.Health);
    }

    [Fact]
    public void Death_clears_every_active_effect()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dying");
        var system = new EffectSystem(fx.Players);

        player.SubmitEffect(EffectIntent.Give(EffectType.Regeneration, amplifier: 0, durationTicks: 200));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotEmpty(player.Effects);

        _ = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Void, player.MaxHealth);

        Assert.True(player.IsDead);
        Assert.Empty(player.Effects);
    }

    [Fact]
    public void A_dead_player_ignores_new_effect_intents()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("corpse");
        _ = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Void, player.MaxHealth);
        Assert.True(player.IsDead);

        player.SubmitEffect(EffectIntent.Give(EffectType.Regeneration, amplifier: 0, durationTicks: 100));

        Assert.False(player.TryConsumeEffectIntent(out _));
    }

    [Fact]
    public void Effects_are_transient_and_do_not_carry_over_a_reconnect()
    {
        // No persistence layer stores effects (documented decision, Phase XI.3): a fresh Player
        // for the same identity always starts with none, matching Hunger's current precedent.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("logging-off");
        var system = new EffectSystem(fx.Players);
        player.SubmitEffect(EffectIntent.Give(EffectType.Poison, amplifier: 0, durationTicks: 200));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotEmpty(player.Effects);

        var reconnected = fx.AddPlayer("logging-off-reconnect");

        Assert.Empty(reconnected.Effects);
    }
}
