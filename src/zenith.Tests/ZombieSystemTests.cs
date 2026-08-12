using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public sealed class ZombieSystemTests
{
    [Fact]
    public void BootstrapSpawnsOneConcreteZombieAndMovesTowardPlayer()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new ZombieSystem(fx.World, fx.Players, new ZombieStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var zombie = Assert.Single(system.Zombies.Active);
        var initialDistance = MathF.Abs(zombie.PositionX - player.PositionX);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(zombie.IsActive);
        Assert.True(MathF.Abs(zombie.PositionX - player.PositionX) < initialDistance);
        Assert.NotEqual(0, zombie.EntityId);
        Assert.Equal((ulong)zombie.EntityId, zombie.RuntimeId);
    }

    [Fact]
    public void Zombie_retargets_when_retained_player_becomes_invalid()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first-target");
        var second = fx.AddInGamePlayer("second-target");
        first.PositionX = 0;
        second.PositionX = 10;
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, 4, first.PositionY, 0);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(first.RuntimeId, zombie.TargetPlayerRuntimeId);

        first.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(second.RuntimeId, zombie.TargetPlayerRuntimeId);
        first.IsInGame = true;
    }

    [Fact]
    public void Zombie_drops_target_when_no_valid_player_remains()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-target");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, 4, player.PositionY, 0);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(player.RuntimeId, zombie.TargetPlayerRuntimeId);
        player.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Null(zombie.TargetPlayerRuntimeId);
        Assert.True(zombie.IsActive);
    }

    [Fact]
    public void Zombie_skips_projection_when_authoritative_position_is_unchanged()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stationary-target");
        player.Chunks.Radius = -1;
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, 1, player.PositionY, 0);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online); // spawn and initial projection state
        system.Tick(fx.Clock, fx.Players.Online); // attack range: no movement to project

        Assert.Equal(0, system.ReplicatedMoveCount);
        Assert.True(system.ReplicatedMoveSkippedCount > 0);
    }

    [Fact]
    public void Zombie_chooses_bounded_local_side_step_around_solid_obstacle()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("blocked-target");
        player.PositionX = -2;
        player.PositionZ = 0;
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, 2.5f, player.PositionY, 0);
        Assert.True(store.TryAdd(zombie));
        // A full-height obstacle blocks the direct X route while the neighbouring Z cells remain
        // supported by the flat floor.
        fx.World.SetBlock(1, (int)player.PositionY, 0, Blocks.Stone);
        fx.World.SetBlock(1, (int)player.PositionY + 1, 0, Blocks.Stone);
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        for (var i = 0; i < 20; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.NotEqual(0f, zombie.PositionZ);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(
            (int)MathF.Floor(zombie.PositionX), (int)MathF.Floor(zombie.PositionY), (int)MathF.Floor(zombie.PositionZ)));
    }

    [Fact]
    public void AttackIsValidatedByGameplayOwnerAndDeathRemovesExactlyOnce()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, zombie.Health.Current);

        for (var i = 0; i < 4; i++)
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Empty(store.Active);
        Assert.False(zombie.IsActive);
        Assert.True(zombie.Health.IsDead);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:rotten_flesh"), loot.Id.Value);
        Assert.True(loot.Id.IsItem);
        Assert.Equal(1, loot.Count);
        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(store.Active);
        Assert.Equal(0f, zombie.Health.Current);
    }

    [Fact]
    public void AttackOutsideReachDoesNotMutateZombieHealth()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 10, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(zombie.Health.Maximum, zombie.Health.Current);
        Assert.Single(store.Active);
    }

    [Fact]
    public void LethalDamage_refusesTransitionWhenLootCannotBeStored()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        // A full store cannot accept a new rotten-flesh cell. The source must therefore remain
        // authoritative rather than becoming a death with silently lost loot.
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(i + 100, (int)player.PositionY, 0,
                StackId.FromBlock(Blocks.Dirt), 1, fx.Players.AllocateRuntimeId(), out _));

        Assert.False(system.TryApplyDamage(zombie, DamageSource.Melee, zombie.Health.Maximum, fx.Players.Online));
        Assert.True(zombie.IsActive);
        Assert.Equal(zombie.Health.Maximum, zombie.Health.Current);
        Assert.Equal(FloorDropStore.SoftCap, fx.World.FloorDrops.Count);
    }

    [Fact]
    public void ZombieInReach_damagesPlayerOncePerCooldown()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("target");
        var store = new ZombieStore();
        var zombie = new Zombie(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(zombie));
        var system = new ZombieSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(16f, player.Health);
        for (var i = 0; i < 19; i++)
        {
            fx.Clock.Advance();
            system.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.Equal(16f, player.Health);
        fx.Clock.Advance();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(12f, player.Health);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingZombieReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new ZombieSystem(fx.World, fx.Players, new ZombieStore(), fx.Context.ItemPalette);
        system.Tick(fx.Clock, fx.Players.Online);
        var before = fx.Transport.Captured.Count;

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var player in fx.Players.Online)
            player.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
        Assert.True(first.IsInGame && second.IsInGame);
    }
}
