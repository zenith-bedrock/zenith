using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 003, phase A: a player-caused kill drops experience as orbs; the points are credited when an orb is collected, to one player,
/// once. A full store credits straight away, so experience is never lost.
/// </summary>
public sealed class ExperienceOrbTests
{
    public ExperienceOrbTests() => Blocks.EnsureLoaded();

    private static (IntentTestFixture Fx, Player.Player Player, RecordingClientChannel Client, ExperienceOrbSystem System) Stage(string name = "collector")
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer(name);
        var client = new RecordingClientChannel();
        player.AttachClient(client);
        fx.World.GetBlock((int)MathF.Floor(player.PositionX), (int)MathF.Floor(player.PositionY), (int)MathF.Floor(player.PositionZ));
        return (fx, player, client, new ExperienceOrbSystem(fx.World));
    }

    private static ExperienceOrbEntry Orb(IntentTestFixture fx, Player.Player near, int value, float dx = 1f, float dy = 0.5f)
    {
        var orb = new ExperienceOrbEntry
        {
            EntityId = fx.Players.AllocateRuntimeId(),
            Value = value,
            Pose = new FloorDropMotion(near.PositionX + dx, near.PositionY + dy, near.PositionZ, 0, 0, 0)
        };
        Assert.True(fx.World.ExperienceOrbs.TrySpawn(orb));
        return orb;
    }

    private static void Run(IntentTestFixture fx, ExperienceOrbSystem system, int ticks)
    {
        for (var i = 0; i < ticks && fx.World.ExperienceOrbs.Count > 0; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    private static int TotalPoints(Player.Player player)
    {
        var total = player.ExperiencePoints;
        for (var level = 0; level < player.ExperienceLevel; level++) total += PlayerExperience.PointsToNextLevel(level);
        return total;
    }

    [Fact]
    public void Every_amount_splits_into_vanilla_sizes_that_add_up_exactly()
    {
        int[] sizes = [2477, 1237, 617, 307, 149, 73, 37, 17, 7, 3, 1];
        for (var amount = 1; amount <= 6000; amount++)
        {
            var orbs = ExperienceOrbSizes.Split(amount);
            Assert.Equal(amount, orbs.Sum());
            Assert.All(orbs, size => Assert.Contains(size, sizes));
            for (var i = 1; i < orbs.Count; i++) Assert.True(orbs[i] <= orbs[i - 1], $"{amount}: largest first");
        }

        Assert.Equal([3, 1, 1], ExperienceOrbSizes.Split(5));
        Assert.Empty(ExperienceOrbSizes.Split(0));
    }

    [Fact]
    public void A_kill_spawns_orbs_and_credits_nothing_until_they_are_collected()
    {
        var (fx, player, client, system) = Stage("slayer");
        var zombies = new ZombieSystem(fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette);
        var id = zombies.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(zombies.Stores.Health.TryGet(id, out var health));

        Assert.True(zombies.TryApplyDamage(id, DamageSource.MeleeFrom(player.RuntimeId), health.State.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Equal(ExperienceOrbSizes.Split(5).Count, fx.World.ExperienceOrbs.Count);
        Assert.Equal(5, fx.World.ExperienceOrbs.Active.Sum(orb => orb.Value));
        Assert.Equal(0, TotalPoints(player));
        Assert.Equal(fx.World.ExperienceOrbs.Count, client.Count("SendAddExperienceOrb"));

        Run(fx, system, 400);

        Assert.Equal(0, fx.World.ExperienceOrbs.Count);
        Assert.Equal(5, TotalPoints(player));
    }

    [Fact]
    public void An_orb_is_collected_once_gives_its_points_and_is_removed_and_the_pickup_is_announced()
    {
        var (fx, player, client, system) = Stage();
        var orb = Orb(fx, player, 7);

        Run(fx, system, 100);

        Assert.Equal(0, fx.World.ExperienceOrbs.Count);
        Assert.Equal(7, TotalPoints(player));
        Assert.Contains(client.Of("SendRemoveActor"), args => (long)args[0]! == orb.EntityId);
        Assert.Contains(client.Of("SendSoundCue"), args => (SoundCue)args[0]! == SoundCue.ExperienceOrbPickup);
        Assert.True(client.Count("SendPlayerAttributes") >= 1);

        Run(fx, system, 100); // nothing left to collect twice
        Assert.Equal(7, TotalPoints(player));
    }

    [Fact]
    public void Only_a_player_within_range_attracts_the_orb()
    {
        var (fx, player, _, system) = Stage();
        var orb = Orb(fx, player, 3, dx: 20f);

        Run(fx, system, 300);

        Assert.Equal(1, fx.World.ExperienceOrbs.Count);
        Assert.Equal(0, TotalPoints(player));
        Assert.Null(orb.TargetRuntimeId);
    }

    [Fact]
    public void With_two_players_the_nearest_collects_and_the_other_gets_nothing()
    {
        var (fx, near, _, system) = Stage("near");
        var far = fx.AddInGamePlayer("far");
        far.PositionX = near.PositionX + 6f;
        far.PositionY = near.PositionY;
        far.PositionZ = near.PositionZ;
        Orb(fx, near, 5, dx: 1f);

        Run(fx, system, 200);

        Assert.Equal(5, TotalPoints(near));
        Assert.Equal(0, TotalPoints(far));
    }

    [Fact]
    public void An_orb_nobody_reaches_expires_after_five_minutes_and_gives_nothing()
    {
        var (fx, player, client, system) = Stage();
        var orb = Orb(fx, player, 3, dx: 30f);

        Run(fx, system, ExperienceOrbSystem.DespawnTicks - 1);
        Assert.Equal(1, fx.World.ExperienceOrbs.Count);
        Run(fx, system, 1);

        Assert.Equal(0, fx.World.ExperienceOrbs.Count);
        Assert.Equal(0, TotalPoints(player));
        Assert.Contains(client.Of("SendRemoveActor"), args => (long)args[0]! == orb.EntityId);
    }

    [Fact]
    public void A_full_store_credits_the_killer_directly_and_loses_nothing()
    {
        var (fx, player, _, _) = Stage("slayer");
        for (var i = 0; i < ExperienceOrbStore.SoftCap; i++) Orb(fx, player, 1, dx: 50f);
        var zombies = new ZombieSystem(fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette);
        var id = zombies.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(zombies.Stores.Health.TryGet(id, out var health));

        Assert.True(zombies.TryApplyDamage(id, DamageSource.MeleeFrom(player.RuntimeId), health.State.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Equal(ExperienceOrbStore.SoftCap, fx.World.ExperienceOrbs.Count);
        Assert.Equal(5, TotalPoints(player));
    }

    [Fact]
    public void A_kill_without_an_owner_drops_no_orbs()
    {
        var (fx, player, _, _) = Stage();
        var zombies = new ZombieSystem(fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette);
        var id = zombies.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(zombies.Stores.Health.TryGet(id, out var health));

        Assert.True(zombies.TryApplyDamage(id, DamageSource.Void, health.State.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Equal(0, fx.World.ExperienceOrbs.Count);
    }

    [Fact]
    public void Collecting_enough_orbs_levels_the_player_up_and_conserves_the_total()
    {
        var (fx, player, _, system) = Stage();
        var expected = 0;
        foreach (var value in new[] { 37, 17, 17, 7, 3, 1 })
        {
            Orb(fx, player, value, dx: 0.5f);
            expected += value;
        }

        Run(fx, system, 300);

        Assert.Equal(0, fx.World.ExperienceOrbs.Count);
        Assert.Equal(expected, TotalPoints(player));
        Assert.True(player.ExperienceLevel >= 3);
    }

    [Fact]
    public void An_orb_in_a_column_that_is_not_loaded_waits_without_moving_and_still_ages()
    {
        var (fx, player, client, system) = Stage();
        var orb = new ExperienceOrbEntry
        {
            EntityId = fx.Players.AllocateRuntimeId(),
            Value = 2,
            Pose = new FloorDropMotion(player.PositionX + 4000f, player.PositionY, player.PositionZ + 4000f, 0, 0, 0)
        };
        Assert.True(fx.World.ExperienceOrbs.TrySpawn(orb));
        var before = orb.Pose;

        Run(fx, system, 10);

        Assert.Equal(before, orb.Pose);
        Assert.Equal(10, orb.AgeTicks);
        Assert.Equal(0, client.Count("SendMoveActorAbsoluteRaw"));
    }
}
