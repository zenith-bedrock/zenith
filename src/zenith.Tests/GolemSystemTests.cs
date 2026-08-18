using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>
/// Phase XVIII, Priority 4 — boss pressure test: high health, a health-threshold phase transition,
/// melee, and a multi-target area slam once enraged. Targeting is a fresh per-tick scan (no
/// retained field), deliberately different from Zombie/Spider's shape — see GolemSystem's doc
/// comment and docs/history/phases/phase-xviii-runtime-pressure-findings.md.
/// ECS-authoritative since the Creeper/Enderman/Golem/Fish migration (docs/decisions.md).
/// </summary>
public sealed class GolemSystemTests
{
    private static Position Pos(GolemSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static HealthState Health(GolemSystem system, EntityId id)
    {
        Assert.True(system.Stores.Health.TryGet(id, out var health));
        return health.State;
    }

    private static bool IsEnraged(GolemSystem system, EntityId id)
    {
        Assert.True(system.GolemStates.TryGet(id, out var state));
        return state.IsEnraged;
    }

    [Fact]
    public void Bootstrap_spawns_one_golem_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("challenger");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Golems);
        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.False(IsEnraged(system, id));
        Assert.True(system.Stores.Identities.TryGet(id, out var identity));
        Assert.NotEqual(0, identity.ActorUniqueId);
        Assert.Equal((ulong)identity.ActorUniqueId, identity.ActorRuntimeId);
    }

    [Fact]
    public void Golem_ignores_a_nearby_player_who_never_provoked_it()
    {
        // Phase XXIII-B — vanilla parity: a naturally-spawned Iron Golem is passive by default.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnGolem(10, player.PositionY, 0);

        var initialX = Pos(system, id).X;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(initialX, Pos(system, id).X);
    }

    [Fact]
    public void Golem_chases_the_player_who_attacked_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnGolem(10, player.PositionY, 0);

        Assert.True(system.TryApplyDamage(id, DamageSource.MeleeFrom(player.RuntimeId), 5f, fx.Players.Online, fx.Clock.CurrentTick));
        var distanceAfterHit = MathF.Abs(Pos(system, id).X - player.PositionX);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(MathF.Abs(Pos(system, id).X - player.PositionX) < distanceAfterHit);
    }

    [Fact]
    public void Golem_chases_a_player_who_recently_attacked_a_nearby_villager()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("villager-attacker");
        player.PositionX = 0;
        player.PositionZ = 0;
        player.LastVillagerAttack = (fx.Clock.CurrentTick, 9f, 0f); // near the golem, not the player
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnGolem(10, player.PositionY, 0);

        var initialDistance = MathF.Abs(Pos(system, id).X - player.PositionX);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(MathF.Abs(Pos(system, id).X - player.PositionX) < initialDistance);
    }

    [Fact]
    public void Golem_ignores_a_villager_attack_that_happened_far_away()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("far-villager-attacker");
        player.PositionX = 0;
        player.PositionZ = 0;
        player.LastVillagerAttack = (fx.Clock.CurrentTick, 500f, 0f); // nowhere near the golem
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnGolem(10, player.PositionY, 0);

        var initialX = Pos(system, id).X;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(initialX, Pos(system, id).X);
    }

    [Fact]
    public void Melee_kill_drops_iron_ingot_and_awards_boss_tier_experience()
    {
        var fx = new IntentTestFixture();
        // Creative: this test is about the many-hits-to-kill loot/XP path, not about surviving the
        // Golem's own retaliation. With real hit-invulnerability now enforced (Phase XXIII-B) a
        // Survival player with default health could die to the Golem's counter-attacks partway
        // through 17 rounds, silently truncating the kill — Creative sidesteps that without changing
        // what this test is actually proving.
        var player = fx.AddInGamePlayer("attacker", GameMode.Creative);
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnGolem(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 17; i++) // 100 health / 6 damage-per-hit
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Golems);
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
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnGolem(0.5f, first.PositionY, 0);
        // Force enrage directly rather than fighting the golem down — this test is about the
        // slam's multi-target behavior, not about re-proving the melee path.
        Assert.True(system.Stores.Health.TryGet(id, out var healthComponent));
        healthComponent.State.Apply(DamageSource.Generic, 51f, fx.Clock.CurrentTick); // 100 -> 49, at/below the 50% threshold

        for (var i = 0; i < 5; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(IsEnraged(system, id));
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
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnGolem(0, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Golems);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingGolemReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new GolemSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
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

    [Fact]
    public void Arrow_can_damage_a_golem_through_the_shared_dispatch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var stores = new EntityRuntime();
        var golems = new GolemSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(golems.Owns, golems.TryApplyDamage);
        var projectiles = new ProjectileSystem(fx.World, fx.Players, stores, damage, new PlayerSpatialIndex());
        var id = golems.SpawnGolem(10f, 100f, 0f); // 100 HP; a 4-damage hit must leave it alive but wounded, not untouched.
        var maxHealth = Health(golems, id).Maximum;

        projectiles.TrySpawnFromActor(owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f, fx.Players.Online);
        projectiles.Tick(fx.Clock, fx.Players.Online);

        Assert.True(Health(golems, id).Current < maxHealth); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(projectiles.Projectiles);
    }
}
