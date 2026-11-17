using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Every species with a <c>melee</c> block in <c>mobs.json</c> hits, cools down and swings through the one
/// <see cref="MobCore.TryMeleeAttack"/>: the numbers are data, the behaviour is shared.
/// </summary>
public sealed class MeleeAttackTests
{
    private static (IntentTestFixture Fx, Player.Player Target, RecordingClientChannel Client) Arena()
    {
        var fx = new IntentTestFixture();
        var target = fx.AddInGamePlayer("victim");
        var client = new RecordingClientChannel();
        target.AttachClient(client);
        target.Chunks.Radius = 1;
        target.Chunks.RememberMany([(0, 0)]);
        return (fx, target, client);
    }

    [Fact]
    public void Only_species_with_a_melee_profile_have_a_melee_cooldown_component()
    {
        var stores = new EntityRuntime();
        var fx = new IntentTestFixture();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var cows = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);

        Assert.True(stores.MeleeCooldowns.Has(zombies.SpawnZombie(0, -60, 0)));
        Assert.False(stores.MeleeCooldowns.Has(cows.SpawnCow(0, -60, 0)));
    }

    [Fact]
    public void A_zombie_hits_for_its_profile_damage_swings_and_then_waits_out_the_cooldown()
    {
        var (fx, target, client) = Arena();
        var melee = MobDefinitions.Zombie.Melee!;
        var zombies = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        zombies.SpawnZombie(target.PositionX + 1f, target.PositionY, target.PositionZ);
        var healthBefore = target.Health;

        zombies.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(healthBefore - melee.Damage, target.Health);
        Assert.Equal(1, client.Count("SendAttackSwing"));

        for (var i = 0; i < melee.CooldownTicks - 1; i++)
        {
            fx.Clock.AdvanceBy(1);
            zombies.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.Equal(healthBefore - melee.Damage, target.Health); // still cooling down

        fx.Clock.AdvanceBy(1);
        zombies.Tick(fx.Clock, fx.Players.Online);
        Assert.True(target.Health < healthBefore - melee.Damage); // cooldown over: second hit (the player's own hit-immunity may trim it)
        Assert.Equal(2, client.Count("SendAttackSwing"));
    }

    [Fact]
    public void A_target_beyond_the_profile_reach_is_not_hit()
    {
        var (fx, target, client) = Arena();
        var stores = new EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var id = zombies.SpawnZombie(target.PositionX + 10f, target.PositionY, target.PositionZ);
        var core = new MobCore(MobDefinitions.Zombie, fx.World, fx.Players, stores, fx.Context.ItemPalette);

        Assert.False(core.TryMeleeAttack(id, target, fx.Clock.CurrentTick, fx.Players.Online));
        Assert.Equal(0, client.Count("SendAttackSwing"));
    }

    [Fact]
    public void An_aggravated_enderman_swings_when_it_hits_like_every_other_melee_species()
    {
        var (fx, target, client) = Arena();
        var endermen = new EndermanSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(3));
        var id = endermen.SpawnEnderman(target.PositionX + 1f, target.PositionY, target.PositionZ);
        ref var state = ref endermen.EndermanStates.GetRef(id);
        state.AggroTargetRuntimeId = target.RuntimeId;
        state.AggroTicksRemaining = 100;
        var healthBefore = target.Health;

        endermen.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(healthBefore - MobDefinitions.Enderman.Melee!.Damage, target.Health);
        Assert.Equal(1, client.Count("SendAttackSwing"));
    }

    [Fact]
    public void Melee_profiles_come_from_the_data_file()
    {
        Assert.Equal(new MeleeProfile(3f, 2.25f, 20), MobDefinitions.Zombie.Melee);
        Assert.Equal(new MeleeProfile(3f, 2.25f, 20), MobDefinitions.Spider.Melee);
        Assert.Equal(new MeleeProfile(7f, 2.5f, 20, 21f), MobDefinitions.Golem.Melee);
        Assert.Equal(new MeleeProfile(7f, 2.25f, 20), MobDefinitions.Enderman.Melee);
        Assert.Null(MobDefinitions.Cow.Melee);
    }
}
