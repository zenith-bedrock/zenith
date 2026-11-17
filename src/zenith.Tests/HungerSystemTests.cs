using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Phase XI.1 — food consumption, exhaustion accrual, starvation and well-fed regen.</summary>
public class HungerSystemTests
{
    [Fact]
    public void Saturation_depletion_sends_attributes_even_when_hunger_does_not_change()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("saturation-sync");
        var client = new RecordingClientChannel(); player.AttachClient(client);
        player.Exhaustion = 4.1f;
        fx.Clock.AdvanceBy(1); // not a periodic exhaustion refresh
        new HungerSystem(fx.Context.ItemPalette, fx.Players).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(20f, player.Hunger); Assert.Equal(4f, player.Saturation);
        Assert.Single(client.Calls, c => c.Call == "SendPlayerAttributes");
    }

    [Fact]
    public void Sprint_exhaustion_is_refreshed_once_per_second_instead_of_each_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("exhaustion-cadence");
        var client = new RecordingClientChannel(); player.AttachClient(client);
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Exhaustion = 0.5f;
        for (var tick = 1; tick <= 20; tick++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
            Assert.Equal(tick == 20 ? 1 : 0, client.Calls.Count(c => c.Call == "SendPlayerAttributes"));
        }
    }

    [Fact]
    public void Exhaustion_from_other_gameplay_is_included_in_periodic_refresh()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("external-exhaustion");
        var client = new RecordingClientChannel(); player.AttachClient(client);
        player.Exhaustion = 2f;
        fx.Clock.AdvanceBy(20);
        new HungerSystem(fx.Context.ItemPalette, fx.Players).Tick(fx.Clock, fx.Players.Online);
        Assert.Single(client.Calls, c => c.Call == "SendPlayerAttributes");
        Assert.Equal(2f, player.Exhaustion);
    }

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

        player.Intents.SubmitEatIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(10f, player.Hunger);
        fx.Clock.AdvanceBy(32);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(14f, player.Hunger); // apple nutrition = 4
        Assert.Equal(2, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Drinking_milk_at_full_hunger_returns_the_bucket_and_asks_for_the_effects_to_be_cleared()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("milk");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:milk_bucket"), 1);
        player.SelectedHotbarSlot = 0;

        player.Intents.SubmitEatIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(32);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Hunger);
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:bucket"), player.Inventory.Get(0).Id.Value);
        Assert.True(player.Effects.TryConsumeEffectIntent(out var intent));
        Assert.True(intent.ClearAll);
    }

    [Fact]
    public void Eating_at_full_hunger_does_not_consume_the_item()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("full");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Inventory.TrySetItem(0, Apple(fx), 1);
        player.SelectedHotbarSlot = 0;

        player.Intents.SubmitEatIntent();
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

        player.Intents.SubmitEatIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        fx.Clock.AdvanceBy(32);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(14f, player.Hunger);
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Crossing_the_exhaustion_threshold_depletes_one_hunger_when_saturation_is_gone()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sprinter");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Saturation = 0f; // isolate the hunger-depletion path — see the saturation-first test below
        player.Exhaustion = 4.05f; // what 40 metres of sprinting add up to

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
        Assert.Equal(5f, player.Saturation); // default
        player.Exhaustion = 4.05f;

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
        player.Saturation = 0f; // saturation would make this the fast regeneration
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
        player.Saturation = 0f;
        player.Exhaustion = 0f;
        _ = player.ApplyDamage(DamageSource.Generic, 5f, 0);

        fx.Clock.AdvanceBy(80);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(16f, player.Health);
        Assert.Equal(6f, player.Exhaustion);
    }

    [Fact]
    public void Full_hunger_with_saturation_heals_every_half_second_and_pays_with_exhaustion()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fast-regen");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 20f;
        player.Saturation = 6f;
        player.Exhaustion = 0f;
        _ = player.ApplyDamage(DamageSource.Generic, 5f, 0);

        fx.Clock.AdvanceBy(9);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(15f, player.Health);

        fx.Clock.AdvanceBy(1); // tick 10
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, player.Health);
        Assert.Equal(6f, player.Exhaustion);
    }

    [Fact]
    public void Fast_regeneration_needs_saturation_and_full_hunger()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("no-fast-regen");
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        player.Hunger = 19f;
        player.Saturation = 6f;
        _ = player.ApplyDamage(DamageSource.Generic, 5f, 0);

        fx.Clock.AdvanceBy(10);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(15f, player.Health); // hunger 19 only has the slow regeneration, at tick 80
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

/// <summary>Vanilla exhaustion sources: by the metre while sprinting or swimming, per jump, per landed attack; not for walking or non-attack damage.</summary>
public class ExhaustionSourceTests
{
    public ExhaustionSourceTests() => Blocks.EnsureLoaded();

    private static void Step(IntentTestFixture fx, MovementSystem system, Player.Player player, float x, float y, float z, bool onGround)
    {
        var input = MovementInputState.From(x, y, z, 0f, 0f);
        input.OnGround = onGround;
        player.Intents.SubmitMovementInput(input);
        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);
    }

    private static (IntentTestFixture Fx, Player.Player Player, MovementSystem System) Stage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mover");
        var system = new MovementSystem(fx.Players);
        Step(fx, system, player, 0f, Blocks.FlatSpawnY, 0f, true);
        player.Exhaustion = 0f;
        return (fx, player, system);
    }

    [Fact]
    public void Sprinting_costs_a_tenth_per_metre_and_not_per_tick()
    {
        var (fx, player, system) = Stage();
        player.IsSprinting = true;

        Step(fx, system, player, 0.3f, Blocks.FlatSpawnY, 0f, true);
        Step(fx, system, player, 0.3f, Blocks.FlatSpawnY, 0f, true); // standing still: nothing
        Step(fx, system, player, 1.3f, Blocks.FlatSpawnY, 0f, true);

        Assert.Equal(0.13f, player.Exhaustion, 3);
    }

    [Fact]
    public void Walking_costs_nothing()
    {
        var (fx, player, system) = Stage();

        for (var i = 1; i <= 20; i++) Step(fx, system, player, i * 0.2f, Blocks.FlatSpawnY, 0f, true);

        Assert.Equal(0f, player.Exhaustion);
    }

    [Fact]
    public void A_jump_costs_less_than_a_sprinting_jump_and_a_teleport_costs_nothing()
    {
        var (fx, player, system) = Stage();
        Step(fx, system, player, 0f, Blocks.FlatSpawnY + 0.42f, 0f, false);
        Assert.Equal(HungerSystem.JumpExhaustion, player.Exhaustion, 4);

        Step(fx, system, player, 0f, Blocks.FlatSpawnY, 0f, true);
        player.Exhaustion = 0f;
        player.IsSprinting = true;
        Step(fx, system, player, 0f, Blocks.FlatSpawnY + 0.42f, 0f, false);
        Assert.Equal(HungerSystem.SprintJumpExhaustion, player.Exhaustion, 4);

        Step(fx, system, player, 0f, Blocks.FlatSpawnY, 0f, true);
        player.Exhaustion = 0f;
        Step(fx, system, player, 300f, Blocks.FlatSpawnY, 0f, true); // a correction, not a run
        Assert.Equal(0f, player.Exhaustion);
    }

    [Fact]
    public void Creative_players_never_tire_from_moving()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative", GameMode.Creative);
        var system = new MovementSystem(fx.Players);
        Step(fx, system, player, 0f, Blocks.FlatSpawnY, 0f, true);
        player.IsSprinting = true;

        Step(fx, system, player, 4f, Blocks.FlatSpawnY, 0f, true);

        Assert.Equal(0f, player.Exhaustion);
    }

    [Fact]
    public void A_landed_attack_costs_a_tenth()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fighter");
        var cows = new Zenith.Gameplay.Entities.CowSystem(fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette);
        cows.SpawnCow(player.PositionX + 1, player.PositionY, player.PositionZ);
        player.Exhaustion = 0f;

        player.Intents.SubmitAttackIntent();
        cows.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(HungerSystem.AttackExhaustion, player.Exhaustion, 4);
    }

    [Fact]
    public void Only_attack_and_fire_damage_exhaust_not_a_fall_or_hunger()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("hurt");
        player.Exhaustion = 0f;

        PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Fall, 1f, 100).Conclude(fx.Players.Online);
        PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Starve, 1f, 200).Conclude(fx.Players.Online);
        PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Drown, 1f, 300).Conclude(fx.Players.Online);
        Assert.Equal(0f, player.Exhaustion);

        PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Melee, 1f, 400).Conclude(fx.Players.Online);
        Assert.Equal(HungerSystem.DamageExhaustion, player.Exhaustion, 4);
    }
}
