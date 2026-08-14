using Zenith.Ecs;
using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XVII, Priority 2 — second proximity-chase-and-attack mob. Structurally near-identical to
/// ZombieSystemTests by design: the point is to prove the acquisition/chase/attack shape really is
/// duplicated once it exists twice (see SpiderSystem's doc comment and
/// docs/history/phases/phase-xvii-runtime-pressure-findings.md), not to explore new Spider-specific behavior.
/// ECS-authoritative since Phase XXII.
/// </summary>
public sealed class SpiderSystemTests
{
    private static Position Pos(SpiderSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static HealthState Health(SpiderSystem system, EntityId id)
    {
        Assert.True(system.Stores.Health.TryGet(id, out var health));
        return health.State;
    }

    private static long? Target(SpiderSystem system, EntityId id)
    {
        Assert.True(system.SpiderStates.TryGet(id, out var state));
        return state.TargetPlayerRuntimeId;
    }

    [Fact]
    public void BootstrapSpawnsOneConcreteSpiderAndMovesTowardPlayer()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Spiders);
        var initial = Pos(system, id);
        var initialDistanceSquared =
            (initial.X - player.PositionX) * (initial.X - player.PositionX) +
            (initial.Z - player.PositionZ) * (initial.Z - player.PositionZ);
        system.Tick(fx.Clock, fx.Players.Online);
        var later = Pos(system, id);
        var laterDistanceSquared =
            (later.X - player.PositionX) * (later.X - player.PositionX) +
            (later.Z - player.PositionZ) * (later.Z - player.PositionZ);

        Assert.True(system.Stores.Entities.IsAlive(id));
        Assert.True(laterDistanceSquared < initialDistanceSquared);
    }

    [Fact]
    public void Spider_retargets_when_retained_player_becomes_invalid()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first-target");
        var second = fx.AddInGamePlayer("second-target");
        first.PositionX = 0;
        second.PositionX = 10;
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnSpider(4, first.PositionY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(first.RuntimeId, Target(system, id));

        first.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(second.RuntimeId, Target(system, id));
        first.IsInGame = true;
    }

    [Fact]
    public void Melee_kill_drops_string_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnSpider(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 8; i++) // 16 health / 2 damage-per-hit
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.Empty(system.Spiders);
        Assert.False(system.Stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:string"), loot.Id.Value);
        Assert.Equal(1, loot.Count);
        Assert.Equal(5, player.ExperiencePoints);
    }

    [Fact]
    public void AttackOutsideReachDoesNotMutateSpiderHealth()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnSpider(player.PositionX + 10, player.PositionY, player.PositionZ);
        var maxHealth = Health(system, id).Maximum;

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(maxHealth, Health(system, id).Current);
        Assert.Single(system.Spiders);
    }

    [Fact]
    public void SpiderInReach_damagesPlayerOncePerCooldown()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnSpider(player.PositionX + 1, player.PositionY, player.PositionZ);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(18f, player.Health);
        for (var i = 0; i < 19; i++)
        {
            fx.Clock.Advance();
            system.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.Equal(18f, player.Health);
        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, player.Health);
    }

    [Fact]
    public void A_spider_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-target");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnSpider(0, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Spiders);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingSpiderReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        // Spider's bootstrap spawn is negative-X/negative-Z of the player, unlike Zombie/Cow's
        // positive offset — remember every chunk the spawn could land in.
        first.Chunks.RememberMany([(0, 0), (-1, -1), (-1, 0), (0, -1)]);
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.Tick(fx.Clock, fx.Players.Online);
        var before = fx.Transport.Captured.Count;

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0), (-1, -1), (-1, 0), (0, -1)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
        Assert.True(first.IsInGame && second.IsInGame);
    }

    [Fact]
    public void A_landed_bite_can_poison_the_player_via_the_shared_effects_dictionary()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bitten");
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(2));
        system.SpawnSpider(player.PositionX + 1, player.PositionY, player.PositionZ);
        // 40 landed hits at a 30% poison chance each: P(never triggers) = 0.7^40 ≈ 5e-7 —
        // deterministic in practice, not a meaningful flake risk, without hard-coding a seed's
        // exact draw sequence (which would break the moment System.Random's algorithm changes).

        for (var i = 0; i < 40; i++)
        {
            system.Tick(fx.Clock, fx.Players.Online);
            player.Heal(player.MaxHealth); // stay alive for the full run — every hit is a fresh poison roll, not just the ones before death
            fx.Clock.AdvanceBy(20); // clears the attack cooldown between attempts
        }

        Assert.True(system.PoisonInflictedCount > 0);
        Assert.True(player.Effects.ContainsKey(EffectType.Poison));
    }

    [Fact]
    public void Melee_kill_before_any_bite_lands_never_poisons_the_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("safe-attacker");
        var system = new SpiderSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(2));
        system.SpawnSpider(player.PositionX + 10, player.PositionY, player.PositionZ);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.PoisonInflictedCount);
        Assert.False(player.Effects.ContainsKey(EffectType.Poison));
    }
}
