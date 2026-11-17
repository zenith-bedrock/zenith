using System.Text;
using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 005 phase D: attributes with modifier stacks, the effects table, the body shared by players and mobs, and the damage breakdown.</summary>
public sealed class BodyTests
{
    public BodyTests() => Blocks.EnsureLoaded();

    // ---- attributes -----------------------------------------------------------------------------------------------

    [Fact]
    public void An_attribute_adds_then_scales_the_base_then_scales_the_total()
    {
        var attribute = new AttributeInstance("minecraft:test", 10f, 0f, 100f);
        attribute.Set(new AttributeModifier("a", 5f, AttributeOperation.Add));
        Assert.Equal(15f, attribute.Value);
        attribute.Set(new AttributeModifier("b", 0.5f, AttributeOperation.MultiplyBase));
        Assert.Equal(22.5f, attribute.Value); // 15 + 15 * 0.5
        attribute.Set(new AttributeModifier("c", 1f, AttributeOperation.MultiplyTotal));
        Assert.Equal(45f, attribute.Value);   // times (1 + 1)
        attribute.Set(new AttributeModifier("a", 25f, AttributeOperation.Add)); // same id replaces
        Assert.Equal(100f, attribute.Value, 3); // clamped to the range
        Assert.True(attribute.Remove("b"));
        Assert.False(attribute.Remove("b"));
        Assert.Equal(70f, attribute.Value);     // (10 + 25) * 2
    }

    [Fact]
    public void Removing_by_prefix_takes_every_modifier_of_one_kind_of_source()
    {
        var attribute = new AttributeInstance("minecraft:test", 1f, 0f, 100f);
        attribute.Set(new AttributeModifier("effect:strength", 3f, AttributeOperation.Add));
        attribute.Set(new AttributeModifier("effect:weakness", -1f, AttributeOperation.Add));
        attribute.Set(new AttributeModifier("item:sword", 2f, AttributeOperation.Add));
        Assert.Equal(2, attribute.RemoveFrom("effect:"));
        Assert.Equal(3f, attribute.Value);
    }

    // ---- the table ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_shipped_table_has_a_row_for_every_effect_and_the_rules_it_describes()
    {
        var table = EffectTable.Default;
        foreach (var type in Enum.GetValues<EffectType>()) Assert.Equal(type, table.Of(type).Type);
        Assert.Equal(25f, table.Of(EffectType.Poison).Periodic!.IntervalTicks);
        Assert.Equal(1f, table.Of(EffectType.Poison).Periodic!.Floor);
        Assert.Equal(DamageCause.Wither, table.Of(EffectType.Wither).Periodic!.Cause);
        Assert.Equal(4f, table.Of(EffectType.Absorption).Absorption);
        Assert.Equal(EffectCategory.Harmful, table.Of(EffectType.Slowness).Category);
        Assert.True(table.TryFind("health_boost", out var boost));
        Assert.Equal(EffectType.HealthBoost, boost.Type);
    }

    [Theory]
    [InlineData("""{"effects":[{"name":"a","id":99,"category":"beneficial"}]}""", "no EffectType")]
    [InlineData("""{"effects":[{"name":"a","id":1,"category":"lucky"}]}""", "unknown category")]
    [InlineData("""{"effects":[{"name":"a","id":1,"category":"beneficial","modifiers":[{"attribute":"minecraft:luck","operation":"add","amount":1}]}]}""", "unknown attribute")]
    [InlineData("""{"effects":[{"name":"a","id":1,"category":"beneficial","modifiers":[{"attribute":"minecraft:health","operation":"set","amount":1}]}]}""", "unknown operation")]
    [InlineData("""{"effects":[{"name":"a","id":1,"category":"harmful","periodic":{"kind":"damage","cause":"nope","amount":1,"intervalTicks":5}}]}""", "unknown damage cause")]
    [InlineData("""{"effects":[{"name":"a","id":1,"category":"harmful"},{"name":"b","id":1,"category":"harmful"}]}""", "listed twice")]
    [InlineData("""{"effects":[{"name":"a","id":1,"category":"harmful"}]}""", "has no row")]
    public void A_bad_table_fails_loudly(string json, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => EffectTable.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        Assert.Contains("effects.json", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    // ---- players: health boost, absorption, the breakdown ----------------------------------------------------------

    private static void GiveThrough(IntentTestFixture fx, Player.Player player, EffectType type, int amplifier, int ticks = 1000)
    {
        player.Effects.SubmitEffect(EffectIntent.Give(type, amplifier, ticks));
        fx.Clock.AdvanceBy(1);
        new EffectSystem(fx.Players, fx.Context.Gate).Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public void Health_boost_raises_the_maximum_and_losing_it_brings_health_down_to_the_old_maximum()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("boosted");
        GiveThrough(fx, player, EffectType.HealthBoost, 1); // level 2: +8
        Assert.Equal(28f, player.MaxHealth);
        player.Heal(8f);
        Assert.Equal(28f, player.Health);

        player.Effects.RemoveEffect(EffectType.HealthBoost);
        Assert.Equal(20f, player.MaxHealth);
        Assert.Equal(20f, player.Health);
    }

    [Fact]
    public void A_boost_tells_the_client_its_new_maximum_and_the_absorption_it_holds()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shown");
        var client = new RecordingClientChannel();
        player.AttachClient(client);

        GiveThrough(fx, player, EffectType.HealthBoost, 0);
        GiveThrough(fx, player, EffectType.Absorption, 1);

        Assert.Contains(client.Of("SendPlayerAttributes"), _ => true);
        Assert.Equal(24f, player.MaxHealth);
        Assert.Equal(8f, player.Body.Health.Absorption);
    }

    [Fact]
    public void The_attribute_packet_carries_the_maximum_and_the_absorption()
    {
        var packet = UpdateAttributesPacket.CreateDefaults(1, 24f, 20f, maxHealth: 28f, absorption: 6f);
        var health = packet.Attributes.Single(a => a.Name == "minecraft:health");
        var absorption = packet.Attributes.Single(a => a.Name == "minecraft:absorption");
        Assert.Equal(28f, health.Max);
        Assert.Equal(6f, absorption.Value);
    }

    [Fact]
    public void Absorption_takes_a_hit_before_health_and_goes_with_the_effect()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shielded");
        GiveThrough(fx, player, EffectType.Absorption, 0); // 4 points
        Assert.Equal(4f, player.Body.Health.Absorption);

        var result = PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Generic, 6f, fx.Clock.CurrentTick);

        Assert.Equal(PlayerDamageOutcome.Hurt, result.Outcome);
        Assert.Equal(18f, player.Health); // 6 - 4 taken by the shield
        Assert.Equal(0f, player.Body.Health.Absorption);
        Assert.Equal(4f, result.Breakdown!.Absorbed);
        var step = result.Breakdown.Of(DamageStage.Absorption)!.Value;
        Assert.Equal((6f, 2f), (step.Before, step.After));
    }

    [Fact]
    public void A_hit_the_shield_swallows_whole_still_counts_as_a_hit_and_expiry_removes_the_rest_of_the_shield()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("shielded");
        GiveThrough(fx, player, EffectType.Absorption, 1, ticks: 50); // 8 points
        var result = PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Generic, 3f, fx.Clock.CurrentTick);
        Assert.True(result.Applied);
        Assert.Equal(20f, player.Health);
        Assert.Equal(5f, player.Body.Health.Absorption);

        fx.Clock.AdvanceBy(60);
        new EffectSystem(fx.Players, fx.Context.Gate).Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(player.Effects.Active);
        Assert.Equal(0f, player.Body.Health.Absorption);
    }

    [Fact]
    public void The_breakdown_names_what_each_stage_did_in_pipeline_order()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("tank");
        player.Effects.ApplyOrRefreshEffect(EffectType.Resistance, 1, fx.Clock.CurrentTick + 1000, fx.Clock.CurrentTick); // two levels: 40% off

        var result = PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Generic, 10f, fx.Clock.CurrentTick);

        var steps = result.Breakdown!.Steps;
        Assert.Equal([DamageStage.Armor, DamageStage.Resistance, DamageStage.Absorption], steps.Select(s => s.Stage));
        Assert.Equal(10f, result.Breakdown.Initial);
        Assert.Equal(6f, result.Breakdown.Of(DamageStage.Resistance)!.Value.After, 3);
        Assert.Equal(6f, result.Breakdown.Final, 3);
        Assert.Equal(14f, player.Health, 3);
    }

    [Fact]
    public void Creative_and_fire_immunity_stop_a_hit_at_their_stage()
    {
        var fx = new IntentTestFixture();
        var creative = fx.AddInGamePlayer("creative");
        creative.SetGameMode(GameMode.Creative);
        var gated = PlayerDamage.ApplyCore(creative, fx.Players, DamageSource.Generic, 5f, 1);
        Assert.False(gated.Applied);
        Assert.Equal(DamageStage.Gate, gated.Breakdown!.Steps.Single().Stage);

        var immune = fx.AddInGamePlayer("immune");
        immune.Effects.ApplyOrRefreshEffect(EffectType.FireResistance, 0, 5000, 0);
        var burn = PlayerDamage.ApplyCore(immune, fx.Players, DamageSource.Lava, 4f, 1);
        Assert.False(burn.Applied);
        Assert.Equal(DamageStage.FireImmunity, burn.Breakdown!.Steps.Single().Stage);
    }

    [Fact]
    public void Wither_goes_around_armor_but_not_around_resistance()
    {
        Assert.False(DamageCause.Wither.ReducedByArmour());
        Assert.True(DamageCause.Wither.ReducedByResistance());
        Assert.False(DamageCause.Freezing.ReducedByArmour());
        Assert.True(DamageCause.Lightning.ReducedByArmour());
        Assert.False(DamageCause.Starve.ReducedByResistance());
        Assert.True(DamageCause.Lava.IsFire() && !DamageCause.Contact.IsFire());
    }

    [Fact]
    public void Strength_and_weakness_are_modifiers_on_the_attack_attribute()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("striker");
        player.Effects.ApplyOrRefreshEffect(EffectType.Strength, 1, 5000, 0);
        var attack = player.Body.Attributes[Attr.AttackDamage];
        Assert.Equal(7f, attack.Value);
        Assert.Contains(attack.Modifiers, m => m.Id == "effect:strength" && m.Amount == 6f);
        player.Effects.RemoveEffect(EffectType.Strength);
        Assert.Empty(attack.Modifiers);

        player.Effects.ApplyOrRefreshEffect(EffectType.Speed, 0, 5000, 0);
        Assert.Equal(0.12f, player.Body.Attributes.Value(Attr.Movement), 4); // +20% of 0.1
    }

    // ---- mobs: the same body -----------------------------------------------------------------------------------------

    private static async Task<(IntentTestFixture Fx, EntityRuntime Stores, ZombieSystem Zombies, DamageDispatch Damage, MobEffectSystem Effects, EntityId Id)> Mob()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, 199, z, Blocks.Stone);
                for (var y = 200; y < 212; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var stores = new EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies);
        damage.Mitigate = (id, source, amount) => MobMitigation.Apply(stores, id, source, amount);
        var id = zombies.SpawnZombie(8.5f, 200f, 8.5f);
        return (fx, stores, zombies, damage, new MobEffectSystem(stores, damage), id);
    }

    private static float HealthOf(EntityRuntime stores, EntityId id) => stores.Health.TryGet(id, out var h) ? h.State.Current : -1f;

    [Fact]
    public async Task Poison_on_a_zombie_hurts_it_on_the_table_schedule_and_leaves_one_point()
    {
        var (fx, stores, _, _, effects, id) = await Mob();
        var start = HealthOf(stores, id);
        Assert.True(effects.Give(id, EffectType.Poison, 0, 1000, fx.Clock, fx.Players.Online));

        for (var i = 0; i < 200; i++)
        {
            fx.Clock.AdvanceBy(1);
            effects.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(HealthOf(stores, id) < start);
        Assert.True(HealthOf(stores, id) >= 1f);
    }

    [Fact]
    public async Task Wither_can_kill_a_zombie_and_regeneration_mends_one()
    {
        var (fx, stores, _, _, effects, id) = await Mob();
        stores.Health.TryGet(id, out var health);
        health.State.Apply(DamageSource.Generic, 6f, 0);
        var hurt = HealthOf(stores, id);
        Assert.True(effects.Give(id, EffectType.Regeneration, 1, 1000, fx.Clock, fx.Players.Online));
        for (var i = 0; i < 100; i++)
        {
            fx.Clock.AdvanceBy(1);
            effects.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(HealthOf(stores, id) > hurt);

        var (fx2, stores2, _, _, effects2, id2) = await Mob();
        Assert.True(effects2.Give(id2, EffectType.Wither, 3, 100000, fx2.Clock, fx2.Players.Online));
        for (var i = 0; i < 2000 && stores2.Health.Has(id2) && HealthOf(stores2, id2) > 0f; i++)
        {
            fx2.Clock.AdvanceBy(1);
            effects2.Tick(fx2.Clock, fx2.Players.Online);
        }

        Assert.False(stores2.Health.TryGet(id2, out var left) && left.State.Current > 0f && !left.State.IsDead);
    }

    [Fact]
    public async Task A_mob_gets_health_boost_absorption_and_resistance_like_a_player()
    {
        var (fx, stores, _, damage, effects, id) = await Mob();
        stores.Health.TryGet(id, out var health);
        var maximum = health.State.Maximum;
        Assert.True(effects.Give(id, EffectType.HealthBoost, 0, 1000, fx.Clock, fx.Players.Online));
        Assert.Equal(maximum + 4f, health.State.Maximum);

        Assert.True(effects.Give(id, EffectType.Absorption, 0, 1000, fx.Clock, fx.Players.Online));
        Assert.Equal(4f, health.State.Absorption);
        Assert.True(effects.Give(id, EffectType.Resistance, 3, 1000, fx.Clock, fx.Players.Online)); // level 4: a fifth of the hit is left
        Assert.True(damage.TryApplyDamage(id, DamageSource.Generic, 5f, fx.Players.Online, fx.Clock.CurrentTick));
        Assert.Equal(3f, health.State.Absorption); // 5 * 0.2 = 1 taken by the shield
    }

    [Fact]
    public async Task A_weaker_effect_does_not_replace_a_stronger_one_on_a_mob_and_instants_act_at_once()
    {
        var (fx, stores, _, _, effects, id) = await Mob();
        Assert.True(effects.Give(id, EffectType.Speed, 2, 1000, fx.Clock, fx.Players.Online));
        Assert.False(effects.Give(id, EffectType.Speed, 0, 1000, fx.Clock, fx.Players.Online));
        stores.TryGetBody(id, out var body);
        Assert.Equal(0.1f * (1f + 0.6f), body.Attributes.Value(Attr.Movement), 4);

        stores.Health.TryGet(id, out var health);
        health.State.Apply(DamageSource.Generic, 5f, 0);
        var before = health.State.Current;
        Assert.True(effects.Give(id, EffectType.InstantHealth, 0, 1, fx.Clock, fx.Players.Online));
        Assert.Equal(before + 4f, health.State.Current);
    }

    [Fact]
    public async Task A_mob_with_nothing_on_it_has_no_body_until_it_is_given_an_effect()
    {
        var (fx, stores, _, _, effects, id) = await Mob();
        Assert.False(stores.Bodies.Has(id));
        effects.Tick(fx.Clock, fx.Players.Online);
        Assert.False(stores.Bodies.Has(id));
        Assert.True(effects.Give(id, EffectType.Speed, 0, 100, fx.Clock, fx.Players.Online));
        Assert.True(stores.Bodies.Has(id));
    }

    [Fact]
    public async Task A_zombie_in_a_helmet_does_not_burn_in_the_sun()
    {
        var (fx, stores, zombies, damage, _, id) = await Mob();
        var hazard = new MobHazardSystem(fx.World, stores, damage, _ => false, zombies.Owns);
        fx.Clock.AdvanceBy(2000);
        stores.Headgear.Set(id, new Headgear());
        for (var i = 0; i < 40; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Equal(0, hazard.BurnTicksOf(id));

        stores.Headgear.Remove(id);
        for (var i = 0; i < 40; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(hazard.BurnTicksOf(id) > 0);
    }
}
