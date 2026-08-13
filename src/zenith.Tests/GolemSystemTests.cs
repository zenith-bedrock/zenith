using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XVIII, Priority 4 — boss pressure test: high health, a health-threshold phase transition,
/// melee, and a multi-target area slam once enraged. Targeting is a fresh per-tick scan (no
/// retained field), deliberately different from Zombie/Spider's shape — see GolemSystem's doc
/// comment and docs/history/phases/phase-xviii-runtime-pressure-findings.md.
/// </summary>
public sealed class GolemSystemTests
{
    [Fact]
    public void Bootstrap_spawns_one_golem_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("challenger");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new GolemSystem(fx.World, fx.Players, new GolemStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var golem = Assert.Single(system.Golems.Active);
        Assert.True(golem.IsActive);
        Assert.False(golem.IsEnraged);
        Assert.NotEqual(0, golem.EntityId);
        Assert.Equal((ulong)golem.EntityId, golem.RuntimeId);
    }

    [Fact]
    public void Golem_chases_the_nearest_player_without_a_retained_target_field()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        player.PositionX = 0;
        player.PositionZ = 0;
        var store = new GolemStore();
        var golem = new Golem(fx.Players.AllocateRuntimeId(), 99, 10, player.PositionY, 0);
        Assert.True(store.TryAdd(golem));
        var system = new GolemSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        var initialDistance = MathF.Abs(golem.PositionX - player.PositionX);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(MathF.Abs(golem.PositionX - player.PositionX) < initialDistance);
    }

    [Fact]
    public void Melee_kill_drops_iron_ingot_and_awards_boss_tier_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var store = new GolemStore();
        var golem = new Golem(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(golem));
        var system = new GolemSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        for (var i = 0; i < 17; i++) // 100 health / 6 damage-per-hit
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Empty(store.Active);
        Assert.False(golem.IsActive);
        Assert.True(golem.Health.IsDead);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:iron_ingot"), loot.Id.Value);
        // 25 raw XP from level 0 crosses two level-up thresholds (7, then 9) — same
        // PlayerExperience.AddPoints math every other kill-XP path already goes through.
        Assert.Equal((2, 9), (player.ExperienceLevel, player.ExperiencePoints));
    }

    [Fact]
    public void Golem_enrages_at_half_health_and_slam_damages_every_nearby_player()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        var second = fx.AddInGamePlayer("second");
        first.PositionX = 0;
        first.PositionZ = 0;
        second.PositionX = 1;
        second.PositionZ = 0;
        var store = new GolemStore();
        var golem = new Golem(fx.Players.AllocateRuntimeId(), 99, 0.5f, first.PositionY, 0);
        // Force enrage directly rather than fighting the golem down — this test is about the
        // slam's multi-target behavior, not about re-proving the melee path.
        golem.ApplyDamage(DamageSource.Generic, 51f); // 100 -> 49, at/below the 50% threshold
        Assert.True(store.TryAdd(golem));
        var system = new GolemSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        for (var i = 0; i < 5; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(golem.IsEnraged);
        Assert.True(system.SlamCount > 0);
        Assert.True(first.Health < 20f);
        Assert.True(second.Health < 20f);
    }

    [Fact]
    public void A_golem_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-challenger");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var store = new GolemStore();
        var golem = new Golem(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY, 0);
        Assert.True(store.TryAdd(golem));
        var system = new GolemSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(golem.IsActive);
        Assert.Empty(system.Golems.Active);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingGolemReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new GolemSystem(fx.World, fx.Players, new GolemStore(), fx.Context.ItemPalette);
        system.Tick(fx.Clock, fx.Players.Online);
        var before = fx.Transport.Captured.Count;

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
        Assert.True(first.IsInGame && second.IsInGame);
    }
}
