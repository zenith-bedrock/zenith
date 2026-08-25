using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Phase XI.1 — food consumption, exhaustion accrual, starvation and well-fed regen.</summary>
public class HungerSystemTests
{
    private static short Apple(IntentTestFixture fx) => fx.Context.ItemPalette.Require("minecraft:apple");

    [Fact]
    public void Eating_held_food_restores_hunger_and_consumes_one_item()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("eater");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Inventory.TrySetItem(0, Apple(fx), 3);
        player.Hunger = 10f;
        player.SelectedHotbarSlot = 0;

        player.SubmitEatIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(14f, player.Hunger); // apple nutrition = 4
        Assert.Equal(2, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Eating_at_full_hunger_does_not_consume_the_item()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("full");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Inventory.TrySetItem(0, Apple(fx), 1);
        player.SelectedHotbarSlot = 0;

        player.SubmitEatIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Hunger);
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Creative_players_eat_without_consuming_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-eater", GameMode.Creative);
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Inventory.TrySetItem(0, Apple(fx), 1);
        player.Hunger = 10f;
        player.SelectedHotbarSlot = 0;

        player.SubmitEatIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(14f, player.Hunger);
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Sprinting_accrues_exhaustion_and_depletes_one_hunger_at_threshold()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sprinter");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.IsSprinting = true;
        player.Saturation = 0f; // isolate the hunger-depletion path — see the saturation-first test below

        for (var i = 0; i < 41; i++) // ~40 * 0.1 exhaustion reaches the 4.0 threshold (float accumulation)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(19f, player.Hunger);
        Assert.True(player.Exhaustion < 4f);
    }

    /// <summary>Phase XXV — vanilla's "well-fed" cushion: exhaustion depletes saturation before it ever touches hunger.</summary>
    [Fact]
    public void Exhaustion_threshold_depletes_saturation_before_hunger()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("well-fed");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.IsSprinting = true;
        Assert.Equal(5f, player.Saturation); // default

        for (var i = 0; i < 41; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Hunger); // untouched — saturation absorbed the crossing
        Assert.Equal(4f, player.Saturation);
    }

    [Fact]
    public void Zero_hunger_deals_periodic_starvation_damage_in_survival()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("starving");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 0f;

        fx.Clock.AdvanceBy(79);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(20f, player.Health); // interval not reached yet

        fx.Clock.AdvanceBy(1); // now at tick 80
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(19f, player.Health);
    }

    /// <summary>
    /// Regression: starvation used to have no health floor and could kill the player outright.
    /// Reference servers gate starvation damage below Hard difficulty so it can never be lethal;
    /// Zenith has no difficulty concept, so it defaults to that common, non-lethal case.
    /// </summary>
    [Fact]
    public void Starvation_never_reduces_health_below_one()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("starving-to-the-brink");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 0f;
        _ = player.ApplyDamage(DamageSource.Generic, 19f, 0); // Health = 1

        fx.Clock.AdvanceBy(80);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsDead);
        Assert.Equal(1f, player.Health);
    }

    [Fact]
    public void Creative_players_never_starve()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("safe", GameMode.Creative);
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 0f;

        fx.Clock.AdvanceBy(80);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Health);
    }

    [Fact]
    public void Well_fed_player_regenerates_health_periodically()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("regen");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 20f;
        _ = player.ApplyDamage(DamageSource.Generic, 5f, 0);

        fx.Clock.AdvanceBy(79);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(15f, player.Health);

        fx.Clock.AdvanceBy(1); // now at tick 80
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, player.Health);
    }

    /// <summary>
    /// Regression: natural well-fed regen used to heal for free. Every vanilla-parity heal costs
    /// exhaustion, same as sprinting/mining/damage — otherwise healing has no food cost at all.
    /// </summary>
    [Fact]
    public void Well_fed_regeneration_costs_exhaustion()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("regen-cost");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 20f;
        player.Exhaustion = 0f;
        _ = player.ApplyDamage(DamageSource.Generic, 5f, 0);

        fx.Clock.AdvanceBy(80);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(16f, player.Health);
        Assert.Equal(6f, player.Exhaustion);
    }

    [Fact]
    public void Low_hunger_player_does_not_regenerate()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("hungry");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 10f;
        _ = player.ApplyDamage(DamageSource.Generic, 5f, 0);

        fx.Clock.AdvanceBy(80);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(15f, player.Health);
    }

    [Fact]
    public void Respawn_resets_hunger_and_exhaustion()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("respawner");
        player.Hunger = 3f;
        player.Exhaustion = 2f;
        _ = player.ApplyDamage(DamageSource.Void, player.MaxHealth, 0);
        Assert.True(player.IsDead);

        player.CompleteRespawn();

        Assert.Equal(20f, player.Hunger);
        Assert.Equal(0f, player.Exhaustion);
    }
}
