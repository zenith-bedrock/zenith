using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// A killed actor must stay visible long enough for the client to play its death animation: the death event goes out at the kill and
/// the removal 20 ticks later, from a corpse entity that no species system or damage query can see.
/// </summary>
public sealed class DeathAnimationTests
{
    private static (IntentTestFixture Fx, RecordingClientChannel Client, EntityRuntime Stores, ZombieSystem Zombies, DeathAnimationSystem Deaths, EntityId Id)
        KilledZombie()
    {
        var fx = new IntentTestFixture();
        var viewer = fx.AddInGamePlayer("viewer");
        var client = new RecordingClientChannel();
        viewer.AttachClient(client);
        viewer.Chunks.Radius = 1;
        viewer.Chunks.RememberMany([(0, 0)]);
        var stores = new EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var id = zombies.SpawnZombie(1f, viewer.PositionY, viewer.PositionZ);
        zombies.Tick(fx.Clock, fx.Players.Online); // the viewer is told about the zombie
        return (fx, client, stores, zombies, new DeathAnimationSystem(stores), id);
    }

    [Fact]
    public void A_kill_announces_death_and_keeps_the_actor_until_the_animation_has_played()
    {
        var (fx, client, stores, zombies, deaths, id) = KilledZombie();
        Assert.True(stores.Identities.TryGet(id, out var identity));
        var killedAt = fx.Clock.CurrentTick;

        Assert.True(zombies.TryApplyDamage(id, DamageSource.Melee, 100f, fx.Players.Online, killedAt));

        Assert.False(stores.Entities.IsAlive(id)); // gameplay is over: no AI, no target, no more damage
        Assert.Equal(1, client.Count("SendDeath"));
        Assert.Equal(0, client.Count("SendRemoveActor")); // the client is still playing the animation
        Assert.Equal(1, deaths.Pending);

        fx.Clock.AdvanceBy(MobDefinitions.DefaultDeathAnimationTicks - 1);
        deaths.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, client.Count("SendRemoveActor"));

        fx.Clock.AdvanceBy(1);
        deaths.Tick(fx.Clock, fx.Players.Online);
        var removed = Assert.Single(client.Of("SendRemoveActor"));
        Assert.Equal(identity.ActorUniqueId, removed[0]);
        Assert.Equal(0, deaths.Pending);

        deaths.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, client.Count("SendRemoveActor")); // exactly once
    }

    [Fact]
    public void A_corpse_is_not_a_zombie_and_cannot_be_hit_again()
    {
        var (fx, _, stores, zombies, _, id) = KilledZombie();
        Assert.True(zombies.TryApplyDamage(id, DamageSource.Melee, 100f, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Empty(zombies.Zombies);
        Assert.False(zombies.TryApplyDamage(id, DamageSource.Melee, 1f, fx.Players.Online, fx.Clock.CurrentTick));
        Assert.Equal(0, stores.Health.Count);
    }

    [Fact]
    public void A_species_without_a_death_animation_is_removed_at_the_kill()
    {
        var fx = new IntentTestFixture();
        var viewer = fx.AddInGamePlayer("viewer");
        var client = new RecordingClientChannel();
        viewer.AttachClient(client);
        viewer.Chunks.Radius = 1;
        viewer.Chunks.RememberMany([(0, 0)]);
        var stores = new EntityRuntime();
        var minecarts = new MinecartSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var id = minecarts.SpawnMinecart(1f, viewer.PositionY, viewer.PositionZ);
        minecarts.Tick(fx.Clock, fx.Players.Online);

        Assert.True(minecarts.TryApplyDamage(id, DamageSource.Melee, 100f, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Equal(1, client.Count("SendRemoveActor"));
        Assert.Equal(0, stores.Corpses.Count);
    }

    [Fact]
    public void Every_species_but_the_minecart_plays_the_death_animation()
    {
        foreach (var definition in MobDefinitions.All)
            Assert.Equal(definition.Kind == MobKind.Minecart ? 0 : MobDefinitions.DefaultDeathAnimationTicks, definition.DeathAnimationTicks);
    }
}
