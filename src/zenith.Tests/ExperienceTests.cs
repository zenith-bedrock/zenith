using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Phase XI.4 — level/points math, mob-kill gain, persistence, reconnect.</summary>
public class ExperienceTests
{
    public ExperienceTests() => Blocks.EnsureLoaded();

    [Theory]
    [InlineData(0, 7)]
    [InlineData(15, 37)]
    [InlineData(16, 42)]
    [InlineData(30, 112)]
    [InlineData(31, 121)]
    public void PointsToNextLevel_matches_vanilla_thresholds(int level, int expected) =>
        Assert.Equal(expected, PlayerExperience.PointsToNextLevel(level));

    [Fact]
    public void AddPoints_accumulates_without_crossing_a_level()
    {
        var (level, points) = PlayerExperience.AddPoints(0, 0, 5);
        Assert.Equal(0, level);
        Assert.Equal(5, points);
    }

    [Fact]
    public void AddPoints_rolls_over_exactly_one_level()
    {
        var (level, points) = PlayerExperience.AddPoints(0, 5, 4); // 7 required for level 0->1
        Assert.Equal(1, level);
        Assert.Equal(2, points);
    }

    [Fact]
    public void AddPoints_rolls_over_multiple_levels_in_one_call()
    {
        var (level, points) = PlayerExperience.AddPoints(0, 0, 100);
        // Cumulative thresholds 7,9,11,13,15,17,19 (levels 0->7) sum to 91; 9 points remain.
        Assert.Equal(7, level);
        Assert.Equal(9, points);
    }

    [Fact]
    public void Progress_reports_a_zero_to_one_fraction_toward_the_next_level()
    {
        Assert.Equal(0f, PlayerExperience.Progress(0, 0));
        Assert.Equal(1f, PlayerExperience.Progress(0, 7)); // clamps at the threshold
        Assert.InRange(PlayerExperience.Progress(0, 3), 0.42f, 0.43f); // 3/7
    }

    [Fact]
    public void Player_AddExperience_applies_level_up_math()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("gainer");

        player.AddExperience(10); // crosses the 7-point threshold for level 0

        Assert.Equal(1, player.ExperienceLevel);
        Assert.Equal(3, player.ExperiencePoints);
    }

    [Fact]
    public void Melee_killing_a_zombie_awards_experience_to_the_attacker()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("slayer");
        var system = new ZombieSystem(fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(system.Stores.Health.TryGet(id, out var health));

        Assert.True(system.TryApplyDamage(
            id, DamageSource.MeleeFrom(player.RuntimeId), health.State.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Equal(0, player.ExperienceLevel);
        Assert.Equal(5, player.ExperiencePoints);
    }

    [Fact]
    public void Melee_killing_a_skeleton_awards_experience_to_the_attacker()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("archer-slayer");
        var ecsStores = new Zenith.Ecs.EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, ecsStores, fx.Context.ItemPalette);
        var minecarts = new MinecartSystem(fx.World, fx.Players, ecsStores, fx.Context.ItemPalette);
        var damage = new Zenith.Gameplay.Entities.DamageDispatch();
        damage.Register(zombies.Owns, zombies.TryApplyDamage);
        damage.Register(minecarts.Owns, minecarts.TryApplyDamage);
        var projectiles = new ProjectileSystem(fx.World, fx.Players, ecsStores, damage, new PlayerSpatialIndex());
        var system = new SkeletonSystem(fx.World, fx.Players, ecsStores, projectiles, fx.Context.ItemPalette);
        var id = system.SpawnSkeleton(player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(ecsStores.Health.TryGet(id, out var health));

        Assert.True(system.TryApplyDamage(
            id, DamageSource.MeleeFrom(player.RuntimeId), health.State.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.False(ecsStores.Entities.IsAlive(id));
        Assert.Equal(5, player.ExperiencePoints);
    }

    [Fact]
    public void Projectile_killing_a_zombie_also_awards_experience()
    {
        var fx = new IntentTestFixture();
        var shooter = fx.AddInGamePlayer("shooter");
        var system = new ZombieSystem(fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(shooter.PositionX + 1, shooter.PositionY, shooter.PositionZ);
        Assert.True(system.Stores.Health.TryGet(id, out var health));

        Assert.True(system.TryApplyDamage(
            id, DamageSource.Projectile(shooter.RuntimeId), health.State.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Equal(5, shooter.ExperiencePoints);
    }

    [Fact]
    public void A_kill_with_no_attributable_owner_grants_no_experience_and_does_not_throw()
    {
        var fx = new IntentTestFixture();
        var bystander = fx.AddInGamePlayer("bystander");
        var system = new ZombieSystem(fx.World, fx.Players, new Zenith.Ecs.EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnZombie(bystander.PositionX + 1, bystander.PositionY, bystander.PositionZ);
        Assert.True(system.Stores.Health.TryGet(id, out var health));

        Assert.True(system.TryApplyDamage(id, DamageSource.Void, health.State.Maximum, fx.Players.Online, fx.Clock.CurrentTick));

        Assert.Equal(0, bystander.ExperiencePoints);
    }

    [Fact]
    public void Experience_persists_and_reloads_through_world_storage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("progressed");
        player.AddExperience(50);

        fx.World.PersistPlayerData(player);

        Assert.True(fx.World.TryLoadPlayerData(
            player.Uuid, out _, out _, out _, out _, out _, out _, out var level, out var points, out _, out _, out _, out _));
        Assert.Equal(player.ExperienceLevel, level);
        Assert.Equal(player.ExperiencePoints, points);
    }

    [Fact]
    public void Reconnect_reloads_the_same_uuids_persisted_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("reconnector");
        player.AddExperience(50);
        fx.World.PersistPlayerData(player);

        var reconnected = fx.AddPlayer("reconnector-again");
        Assert.True(fx.World.TryLoadPlayerData(
            player.Uuid, out _, out _, out _, out _, out _, out _, out var level, out var points, out _, out _, out _, out _));
        reconnected.SetExperience(level, points);

        Assert.Equal(player.ExperienceLevel, reconnected.ExperienceLevel);
        Assert.Equal(player.ExperiencePoints, reconnected.ExperiencePoints);
    }

    [Fact]
    /// <summary>
    /// Phase XXV: previously death never touched XP at all — a real, documented gap from the
    /// earlier cross-reference audit ("might be deliberate, but nothing documents it as one").
    /// Vanilla resets experience to zero on death regardless of gamemode; fixed to match.
    /// </summary>
    public void Death_resets_experience_to_zero()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("hardcore");
        player.AddExperience(50);

        _ = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Void, player.MaxHealth, fx.Clock.CurrentTick);

        Assert.True(player.IsDead);
        Assert.Equal(0, player.ExperienceLevel);
        Assert.Equal(0, player.ExperiencePoints);
    }
}
