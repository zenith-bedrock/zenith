using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 11: falling hits are critical, a weapon that lands a hit wears, armor wears when its wearer is hurt.</summary>
public sealed class GearWearTests
{
    public GearWearTests() => Blocks.EnsureLoaded();

    private static (IntentTestFixture Fx, Player.Player Attacker, Player.Player Target) Duel(string weapon, GameMode attackerMode = GameMode.Survival)
    {
        var fx = new IntentTestFixture();
        var attacker = fx.AddInGamePlayer("attacker", attackerMode);
        var target = fx.AddInGamePlayer("target");
        target.PositionX = attacker.PositionX + 1;
        target.PositionZ = attacker.PositionZ;
        target.PositionY = attacker.PositionY;
        if (weapon.Length > 0)
            Assert.True(attacker.Inventory.TrySet(0, StackId.FromItem(fx.Context.ItemPalette.Require(weapon)), 1));
        return (fx, attacker, target);
    }

    private static void Strike(IntentTestFixture fx, Player.Player attacker, Player.Player target)
    {
        attacker.Intents.SubmitAttackIntent(target.RuntimeId);
        new PlayerMeleeSystem(fx.Players, fx.Context.ItemPalette).Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public void A_hit_while_falling_is_critical_and_one_on_the_ground_is_not()
    {
        var (fx, attacker, target) = Duel("minecraft:iron_sword");
        Strike(fx, attacker, target);
        Assert.Equal(20f - 7f, target.Health);

        var (fx2, falling, victim) = Duel("minecraft:iron_sword");
        falling.IsOnGround = false;
        falling.FallPeakY = falling.PositionY + 1f;
        Strike(fx2, falling, victim);
        Assert.Equal(20f - 7f * 1.5f, victim.Health);
    }

    [Fact]
    public void Rising_is_not_a_critical_hit()
    {
        var (fx, attacker, target) = Duel("minecraft:iron_sword");
        attacker.IsOnGround = false;
        attacker.FallPeakY = attacker.PositionY; // still on the way up: nothing has been fallen yet
        Strike(fx, attacker, target);
        Assert.Equal(20f - 7f, target.Health);
    }

    [Theory]
    [InlineData("minecraft:iron_sword", 1)]
    [InlineData("minecraft:iron_axe", 2)]
    public void A_weapon_that_lands_a_hit_loses_durability(string weapon, int wear)
    {
        var (fx, attacker, target) = Duel(weapon);
        Strike(fx, attacker, target);
        Assert.Equal(wear, attacker.Inventory.Get(0).Id.Damage);
    }

    [Fact]
    public void A_creative_weapon_does_not_wear_and_a_miss_does_not_either()
    {
        var (fx, attacker, target) = Duel("minecraft:iron_sword", GameMode.Creative);
        Strike(fx, attacker, target);
        Assert.Equal(0, attacker.Inventory.Get(0).Id.Damage);

        var (fx2, far, away) = Duel("minecraft:iron_sword");
        away.PositionX = far.PositionX + 10;
        Strike(fx2, far, away);
        Assert.Equal(0, far.Inventory.Get(0).Id.Damage);
    }

    [Fact]
    public void Armor_wears_when_its_wearer_is_hit_and_a_fall_does_not_wear_it()
    {
        var (fx, attacker, target) = Duel("minecraft:iron_sword");
        var chest = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:iron_chestplate"));
        Assert.True(target.Inventory.TrySetArmor(PlayerInventory.ArmorChestplateSlot, chest, 1));

        Strike(fx, attacker, target); // 7 damage: 1 point of wear
        Assert.Equal(1, target.Inventory.GetArmor(PlayerInventory.ArmorChestplateSlot).Id.Damage);

        PlayerDamage.ApplyCore(target, fx.Players, DamageSource.Fall, 5f, fx.Clock.CurrentTick + 100);
        Assert.Equal(1, target.Inventory.GetArmor(PlayerInventory.ArmorChestplateSlot).Id.Damage);
    }

    [Fact]
    public void Heavy_hits_wear_armor_by_a_quarter_of_the_damage()
    {
        var (fx, _, target) = Duel("");
        var helmet = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:iron_helmet"));
        Assert.True(target.Inventory.TrySetArmor(PlayerInventory.ArmorHelmetSlot, helmet, 1));

        PlayerDamage.ApplyCore(target, fx.Players, DamageSource.Explosion, 12f, fx.Clock.CurrentTick + 100);

        Assert.Equal(3, target.Inventory.GetArmor(PlayerInventory.ArmorHelmetSlot).Id.Damage);
    }
}
