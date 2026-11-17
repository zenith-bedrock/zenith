using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Survival;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 26: what the potion effects change in the rules (dig speed, melee damage, resistance, fire, breath, food, wither, instants).</summary>
public sealed class EffectRulesTests
{
    public EffectRulesTests() => Blocks.EnsureLoaded();

    private static Player.Player Give(IntentTestFixture fx, string name, EffectType type, int amplifier = 0, int ticks = 1000)
    {
        var player = fx.AddInGamePlayer(name);
        player.Effects.ApplyOrRefreshEffect(type, amplifier, fx.Clock.CurrentTick + (ulong)ticks, fx.Clock.CurrentTick);
        return player;
    }

    [Fact]
    public void Haste_speeds_digging_a_fifth_a_level_and_fatigue_slows_it_hard()
    {
        var fx = new IntentTestFixture();
        var plain = fx.AddInGamePlayer("plain");
        var haste = Give(fx, "haste", EffectType.Haste, 1);
        var fatigue = Give(fx, "fatigue", EffectType.MiningFatigue, 0);

        Assert.Equal(1.0, EffectRules.DigSpeed(plain));
        Assert.Equal(1.4, EffectRules.DigSpeed(haste), 3);
        Assert.Equal(0.3, EffectRules.DigSpeed(fatigue), 3);
        var held = default(StackId);
        Assert.True(DigPenalty.BreakTicksFor(fatigue, Blocks.Stone, held) > DigPenalty.BreakTicksFor(plain, Blocks.Stone, held));
        Assert.True(DigPenalty.BreakTicksFor(haste, Blocks.Stone, held) < DigPenalty.BreakTicksFor(plain, Blocks.Stone, held));
    }

    [Fact]
    public void Strength_adds_three_a_level_and_weakness_takes_four_but_never_below_nothing()
    {
        var fx = new IntentTestFixture();
        var strong = Give(fx, "strong", EffectType.Strength, 1);
        var weak = Give(fx, "weak", EffectType.Weakness, 0);

        Assert.Equal(6f, EffectRules.MeleeBonus(strong));
        Assert.Equal(-4f, EffectRules.MeleeBonus(weak));
        Assert.Equal(7f, PlayerMeleeDamage.Resolve(strong, fx.Context.ItemPalette)); // a bare hand does 1
        Assert.Equal(0f, PlayerMeleeDamage.Resolve(weak, fx.Context.ItemPalette));
    }

    [Fact]
    public void Resistance_takes_a_fifth_off_a_level_and_level_five_is_immunity_but_the_void_ignores_it()
    {
        var fx = new IntentTestFixture();
        var two = Give(fx, "two", EffectType.Resistance, 1);
        var five = fx.AddInGamePlayer("five");
        five.Effects.ApplyOrRefreshEffect(EffectType.Resistance, 4, 5000, 0);

        Assert.Equal(6f, EffectRules.AfterResistance(two, DamageSource.Generic, 10f), 3);
        Assert.Equal(0f, EffectRules.AfterResistance(five, DamageSource.Generic, 10f));
        Assert.Equal(10f, EffectRules.AfterResistance(five, DamageSource.Void, 10f));

        PlayerDamage.ApplyCore(two, fx.Players, DamageSource.Generic, 10f, 100);
        Assert.Equal(14f, two.Health, 2);
    }

    [Fact]
    public void Fire_resistance_blocks_fire_and_lava_damage_and_the_flames_themselves()
    {
        var fx = new IntentTestFixture();
        var player = Give(fx, "fireproof", EffectType.FireResistance);

        PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Fire, 5f, 100);
        PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Lava, 5f, 200);
        Assert.Equal(20f, player.Health);

        player.BurnTicks = 100;
        new BurnSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, player.BurnTicks);
    }

    [Fact]
    public void Water_breathing_keeps_the_air_full_under_water_and_hunger_and_saturation_change_the_food_bar()
    {
        var fx = new IntentTestFixture();
        var diver = Give(fx, "diver", EffectType.WaterBreathing);
        Assert.True(EffectRules.BreathesUnderWater(diver));

        var hungry = Give(fx, "hungry", EffectType.Hunger, 1);
        var fed = Give(fx, "fed", EffectType.Saturation, 0);
        fed.Hunger = 10f;
        fed.Saturation = 0f;
        var system = new HungerSystem(fx.Context.ItemPalette, fx.Players);
        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0.01f, hungry.Exhaustion, 4);
        Assert.Equal(11f, fed.Hunger);
        Assert.Equal(1f, fed.Saturation);
    }

    [Fact]
    public void Instant_health_mends_and_instant_damage_hurts_without_staying_active()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("instant");
        var system = new EffectSystem(fx.Players, fx.Context.Gate);
        _ = player.ApplyDamage(DamageSource.Generic, 12f, 0);

        player.Effects.SubmitEffect(EffectIntent.Give(EffectType.InstantHealth, 0, 1));
        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(12f, player.Health);

        player.Effects.SubmitEffect(EffectIntent.Give(EffectType.InstantDamage, 0, 1));
        fx.Clock.AdvanceBy(30);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(6f, player.Health);
        Assert.Empty(player.Effects.Active);
    }

    [Fact]
    public void Wither_can_kill_where_poison_cannot()
    {
        var fx = new IntentTestFixture();
        var player = Give(fx, "withering", EffectType.Wither);
        var system = new EffectSystem(fx.Players, fx.Context.Gate);
        _ = player.ApplyDamage(DamageSource.Generic, 19f, 0);

        for (var i = 0; i < 100 && !player.IsDead; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(player.IsDead);
    }

    [Fact]
    public void The_effect_command_knows_the_new_names()
    {
        var fx = new IntentTestFixture();
        var op = fx.AddOperator("op");
        var commands = new Zenith.Gameplay.Commands.CommandRuntime(fx.Players, fx.Context.Permissions);

        commands.Execute(op, "/effect give haste 60 1");
        new EffectSystem(fx.Players, fx.Context.Gate).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, op.Effects.LevelOf(EffectType.Haste));
    }
}
