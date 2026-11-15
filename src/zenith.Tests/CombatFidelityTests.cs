using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Packets;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class CombatFidelityTests
{
    [Theory]
    [InlineData("", 1f)]
    [InlineData("wooden_sword", 5f)]
    [InlineData("diamond_sword", 8f)]
    [InlineData("wooden_axe", 4f)]
    [InlineData("iron_pickaxe", 5f)]
    [InlineData("netherite_shovel", 6f)]
    [InlineData("trident", 9f)]
    public void Damage_comes_from_the_held_item(string name, float damage)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("weapon");
        player.Inventory.Clear();
        if (name != "") player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:" + name), 1);
        Assert.Equal(damage, PlayerMeleeDamage.Resolve(player, fx.Context.ItemPalette));
    }

    [Fact]
    public void Same_sword_deals_equal_base_damage_to_every_mob_species()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("same-attacker");
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:diamond_sword"), 1);
        foreach (var definition in MobDefinitions.All)
        {
            var health = new HealthState(100);
            var view = new DamageableActorView(99, 99, player.PositionX + 1, player.PositionY, player.PositionZ, health);
            player.Intents.SubmitAttackIntent(99);
            Assert.True(DamageableActorCombatCore.TryApplyPlayerMeleeAttack(view, fx.Players.Online,
                definition, fx.Context.ItemPalette, 0,
                (source, amount, _, tick) => health.Apply(source, amount, tick).WasApplied));
            Assert.Equal(92f, health.Current);
        }
    }

    [Fact]
    public void Player_targets_use_the_same_weapon_damage_resolution()
    {
        var fx = new IntentTestFixture(); var attacker = fx.AddInGamePlayer("sword-pvp");
        var target = fx.AddInGamePlayer("target"); target.PositionX = attacker.PositionX + 1;
        attacker.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:diamond_sword"), 1);
        attacker.Intents.SubmitAttackIntent(target.RuntimeId);
        new PlayerMeleeSystem(fx.Players, fx.Context.ItemPalette).Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(12f, target.Health);
    }

    [Fact]
    public void Ordinary_armor_mitigates_melee_but_never_fall_damage()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("armored-fall");
        player.Inventory.TrySetArmor(PlayerInventory.ArmorChestplateSlot,
            StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:diamond_chestplate")), 1);
        Assert.Equal(10f, ArmorMitigation.Apply(player, DamageSource.Fall, 10));
        Assert.True(ArmorMitigation.Apply(player, DamageSource.MeleeFrom(999), 10) < 10);
    }

    [Fact]
    public void Clearing_effects_emits_a_remove_for_each_previous_type_once()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("clear-wire");
        var client = new RecordingClientChannel(); player.AttachClient(client);
        var system = new EffectSystem(fx.Players);
        foreach (var type in new[] { EffectType.Poison, EffectType.Regeneration })
        {
            player.Effects.SubmitEffect(EffectIntent.Give(type, 0, 100)); system.Tick(fx.Clock, fx.Players.Online);
        }
        client.Calls.Clear(); player.Effects.SubmitEffect(EffectIntent.Clear); system.Tick(fx.Clock, fx.Players.Online);
        var removals = client.Calls.Where(c => c.Call == "SendMobEffect").ToArray();
        Assert.Equal(2, removals.Length);
        Assert.All(removals, call => Assert.Equal(MobEffectPacket.EventRemove, Assert.IsType<byte>(call.Args[1])));
        Assert.Equal(new[] { 10, 19 }, removals.Select(c => Assert.IsType<int>(c.Args[2])).Order().ToArray());
        Assert.Empty(player.Effects.Active);
        client.Calls.Clear(); player.Effects.SubmitEffect(EffectIntent.Clear); system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(client.Calls);
    }
}
