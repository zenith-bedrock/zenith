using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XX, Priority 2 — second non-ground-navigation mob: 3D wander like Bat's, but validity
/// requires the destination cell to BE water, a third distinct movement rule. See FishSystem's
/// doc comment and docs/history/phases/phase-xx-runtime-relationships-findings.md.
/// </summary>
public sealed class FishSystemTests
{
    [Fact]
    public void Bootstrap_spawns_one_fish_where_water_exists_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("angler");
        player.PositionX = 0;
        player.PositionY = Blocks.FlatSpawnY;
        player.PositionZ = 0;
        fx.World.SetBlock(4, (int)player.PositionY, 0, Blocks.Water);
        var system = new FishSystem(fx.World, fx.Players, new FishStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var fish = Assert.Single(system.Fish.Active);
        Assert.True(fish.IsActive);
        Assert.NotEqual(0, fish.EntityId);
        Assert.Equal((ulong)fish.EntityId, fish.RuntimeId);
    }

    [Fact]
    public void Bootstrap_does_not_spawn_a_fish_when_no_water_exists()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dry-angler");
        player.PositionX = 0;
        player.PositionY = Blocks.FlatSpawnY;
        player.PositionZ = 0;
        var system = new FishSystem(fx.World, fx.Players, new FishStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(system.Fish.Active);
    }

    [Fact]
    public void Fish_wanders_within_water_but_never_leaves_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-angler");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var world = fx.World;
        var y = Blocks.FlatSpawnY;
        for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
                for (var dy = -1; dy <= 1; dy++)
                    world.SetBlock(x, y + dy, z, Blocks.Water);
        var store = new FishStore();
        var fish = new Fish(fx.Players.AllocateRuntimeId(), 99, 0, y, 0);
        Assert.True(store.TryAdd(fish));
        var system = new FishSystem(world, fx.Players, store, fx.Context.ItemPalette, new Random(1));

        var startX = fish.PositionX;
        var startY = fish.PositionY;
        var startZ = fish.PositionZ;
        for (var i = 0; i < 200; i++)
        {
            system.Tick(fx.Clock, fx.Players.Online);
            Assert.Equal(Blocks.Water, world.GetBlock(
                (int)MathF.Floor(fish.PositionX), (int)MathF.Floor(fish.PositionY), (int)MathF.Floor(fish.PositionZ)));
        }

        Assert.True(fish.PositionX != startX || fish.PositionY != startY || fish.PositionZ != startZ);
    }

    [Fact]
    public void Fish_never_attacks_or_damages_a_nearby_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        fx.World.SetBlock(
            (int)MathF.Floor(player.PositionX), (int)player.PositionY, (int)MathF.Floor(player.PositionZ), Blocks.Water);
        var store = new FishStore();
        var fish = new Fish(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 0.5f, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(fish));
        var system = new FishSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(2));

        for (var i = 0; i < 100; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Health);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void Melee_kill_drops_cod_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("fisher");
        var store = new FishStore();
        var fish = new Fish(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(fish));
        var system = new FishSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        player.SubmitAttackIntent();
        system.Tick(fx.Clock, fx.Players.Online); // 3 health / 4 damage-per-hit — one hit kills

        Assert.Empty(store.Active);
        Assert.False(fish.IsActive);
        Assert.True(fish.Health.IsDead);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:cod"), loot.Id.Value);
        Assert.Equal(1, player.ExperiencePoints);
    }

    [Fact]
    public void A_fish_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-angler");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var store = new FishStore();
        var fish = new Fish(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY, 0);
        Assert.True(store.TryAdd(fish));
        var system = new FishSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(1));

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(fish.IsActive);
        Assert.Empty(system.Fish.Active);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingFishReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new FishSystem(fx.World, fx.Players, new FishStore(), fx.Context.ItemPalette);
        var store = system.Fish;
        var fish = new Fish(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY, 0);
        Assert.True(store.TryAdd(fish));
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
