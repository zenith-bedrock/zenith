using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class GravityTests
{
    public GravityTests() => Blocks.EnsureLoaded();

    private static World.World NewWorld() => new(new InMemoryChunkStorage());

    [Fact]
    public void IsGravity_sand_and_gravel_only()
    {
        Assert.True(Blocks.IsGravity(Blocks.Sand));
        Assert.True(Blocks.IsGravity(Blocks.Gravel));
        Assert.False(Blocks.IsGravity(Blocks.Stone));
        Assert.False(Blocks.IsGravity(Blocks.Air));
        Assert.True(Blocks.IsPlaceable(Blocks.Gravel));
    }

    [Fact]
    public void Pending_soft_cap_refuses_new_keys()
    {
        var store = new GravityPendingStore();
        for (var i = 0; i < GravityPendingStore.SoftCap; i++)
            Assert.True(store.TryEnqueue(i, 64, 0));

        Assert.Equal(GravityPendingStore.SoftCap, store.Count);
        Assert.False(store.TryEnqueue(GravityPendingStore.SoftCap, 64, 0));
        Assert.True(store.TryEnqueue(0, 64, 0)); // refresh existing
        Assert.Equal(GravityPendingStore.SoftCap, store.Count);
    }

    [Fact]
    public void Sand_over_air_falls_onto_support()
    {
        var world = NewWorld();
        // Flat grass at FlatGrassY; place sand two above grass with air gap.
        var supportY = Blocks.FlatGrassY;
        var sandY = supportY + 2; // one air cell between
        Assert.Equal(Blocks.Air, world.GetBlock(0, supportY + 1, 0));
        Assert.True(world.TrySetBlock(0, sandY, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(0, sandY, 0));

        var gravity = new GravitySystem(world);
        gravity.Tick(new GameClock(), Array.Empty<Zenith.Player.Player>());

        Assert.Equal(Blocks.Air, world.GetBlock(0, sandY, 0));
        Assert.Equal(Blocks.Sand, world.GetBlock(0, supportY + 1, 0));
        Assert.Equal(0, world.GravityPending.Count);
    }

    [Fact]
    public void Dig_under_tower_cascades_over_ticks()
    {
        var world = NewWorld();
        var baseY = Blocks.FlatGrassY + 1;
        Assert.True(world.TrySetBlock(1, baseY, 0, Blocks.Sand));
        Assert.True(world.TrySetBlock(1, baseY + 1, 0, Blocks.Sand));
        Assert.True(world.TrySetBlock(1, baseY + 2, 0, Blocks.Sand));
        Assert.True(world.TrySetBlock(1, baseY, 0, Blocks.Air));
        Assert.True(world.GravityPending.TryEnqueue(1, baseY + 1, 0));

        var gravity = new GravitySystem(world);
        var clock = new GameClock();
        gravity.Tick(clock, Array.Empty<Zenith.Player.Player>());
        Assert.Equal(Blocks.Sand, world.GetBlock(1, baseY, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(1, baseY + 1, 0));
        Assert.Equal(Blocks.Sand, world.GetBlock(1, baseY + 2, 0));

        gravity.Tick(clock, Array.Empty<Zenith.Player.Player>());
        Assert.Equal(Blocks.Sand, world.GetBlock(1, baseY, 0));
        Assert.Equal(Blocks.Sand, world.GetBlock(1, baseY + 1, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(1, baseY + 2, 0));
        Assert.Equal(0, world.GravityPending.Count);
    }

    [Fact]
    public void Max_steps_per_tick_caps_falls_per_game_tick()
    {
        var world = NewWorld();
        var baseY = Blocks.FlatGrassY;
        // Six floating sand with a single air gap above grass — enqueue bottom floater only.
        for (var i = 0; i < 6; i++)
            Assert.True(world.TrySetBlock(2, baseY + 2 + i, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(2, baseY + 2, 0));

        var gravity = new GravitySystem(world);
        gravity.Tick(new GameClock(), Array.Empty<Zenith.Player.Player>());

        Assert.Equal(Blocks.Sand, world.GetBlock(2, baseY + 1, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(2, baseY + 2, 0));
        Assert.Equal(Blocks.Sand, world.GetBlock(2, baseY + 3, 0));
        Assert.True(world.GravityPending.Count > 0, "cascade continues on later ticks");
    }

    [Fact]
    public void Unsupported_at_void_floor_destroys()
    {
        var world = NewWorld();
        // Place sand at FlatMinY with nothing below (unsupported → void destroy).
        Assert.True(world.TrySetBlock(2, Blocks.FlatMinY, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(2, Blocks.FlatMinY, 0));

        new GravitySystem(world).Tick(new GameClock(), Array.Empty<Zenith.Player.Player>());

        Assert.Equal(Blocks.Air, world.GetBlock(2, Blocks.FlatMinY, 0));
    }

    [Fact]
    public void Gravel_placeable_and_falls_like_sand()
    {
        var world = NewWorld();
        var supportY = Blocks.FlatGrassY;
        var y = supportY + 2;
        Assert.True(world.TrySetBlock(3, y, 0, Blocks.Gravel));
        Assert.True(world.GravityPending.TryEnqueue(3, y, 0));
        new GravitySystem(world).Tick(new GameClock(), Array.Empty<Zenith.Player.Player>());
        Assert.Equal(Blocks.Gravel, world.GetBlock(3, supportY + 1, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(3, y, 0));
    }

    [Fact]
    public void CreativeCatalog_includes_gravel()
    {
        var catalog = CreativeCatalog.CreateDefault();
        Assert.True(catalog.TryGet(CreativeCatalog.Gravel, out var id, out _));
        Assert.True(id.IsBlock);
        Assert.Equal(Blocks.Gravel, id.Value);
    }

    [Fact]
    public void SettleAllPending_drains_queue()
    {
        var world = NewWorld();
        Assert.True(world.TrySetBlock(4, Blocks.FlatGrassY + 3, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(4, Blocks.FlatGrassY + 3, 0));
        var gravity = new GravitySystem(world);
        gravity.SettleAllPending();
        Assert.Equal(0, world.GravityPending.Count);
        Assert.Equal(Blocks.Sand, world.GetBlock(4, Blocks.FlatGrassY + 1, 0));
    }
}
