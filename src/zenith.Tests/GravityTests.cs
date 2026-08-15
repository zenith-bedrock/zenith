using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Tests;

public class GravityTests
{
    public GravityTests() => Blocks.EnsureLoaded();

    private static World.World NewWorld() => new(new InMemoryChunkStorage());

    private static GravitySystem NewGravity(World.World world) => new(world, new PlayerManager());

    /// <summary>Ticks until every pending/active fall settles, or fails the test past a sane budget.</summary>
    private static void Settle(GravitySystem gravity, World.World world, int maxTicks = 200)
    {
        var clock = new GameClock();
        for (var i = 0; i < maxTicks; i++)
        {
            if (world.GravityPending.Count == 0 && world.FallingBlocks.Active.Count == 0) return;
            gravity.Tick(clock, Array.Empty<Zenith.Player.Player>());
        }

        Assert.Fail($"Gravity did not settle within {maxTicks} ticks.");
    }

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
    public void Starting_a_fall_vacates_source_immediately_but_defers_landing()
    {
        // ADR §95: the fall is a real entity now, not an instant teleport — the source cell
        // empties right away (matches vanilla: the block visually disappears the instant support
        // is lost), but the destination stays untouched until the entity actually arrives.
        var world = NewWorld();
        var supportY = Blocks.FlatGrassY;
        var sandY = supportY + 2;
        Assert.True(world.TrySetBlock(0, sandY, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(0, sandY, 0));

        var gravity = NewGravity(world);
        gravity.Tick(new GameClock(), Array.Empty<Zenith.Player.Player>());

        Assert.Equal(Blocks.Air, world.GetBlock(0, sandY, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(0, supportY + 1, 0)); // not landed yet
        Assert.Single(world.FallingBlocks.Active);
        Assert.Equal(Blocks.Sand, world.FallingBlocks.Active[0].BlockRuntimeId);
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

        var gravity = NewGravity(world);
        Settle(gravity, world);

        Assert.Equal(Blocks.Air, world.GetBlock(0, sandY, 0));
        Assert.Equal(Blocks.Sand, world.GetBlock(0, supportY + 1, 0));
        Assert.Equal(0, world.GravityPending.Count);
        Assert.Empty(world.FallingBlocks.Active);
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

        var gravity = NewGravity(world);
        Settle(gravity, world);

        Assert.Equal(Blocks.Sand, world.GetBlock(1, baseY, 0));
        Assert.Equal(Blocks.Sand, world.GetBlock(1, baseY + 1, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(1, baseY + 2, 0));
        Assert.Equal(0, world.GravityPending.Count);
        Assert.Empty(world.FallingBlocks.Active);
    }

    [Fact]
    public void Max_steps_per_tick_caps_falls_started_per_game_tick()
    {
        var world = NewWorld();
        var baseY = Blocks.FlatGrassY;
        // Six floating sand with a single air gap above grass — enqueue bottom floater only.
        for (var i = 0; i < 6; i++)
            Assert.True(world.TrySetBlock(2, baseY + 2 + i, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(2, baseY + 2, 0));

        var gravity = NewGravity(world);
        // Only the one enqueued cell can start this tick — MaxStepsPerTick caps starts, not landings.
        gravity.Tick(new GameClock(), Array.Empty<Zenith.Player.Player>());
        Assert.Single(world.FallingBlocks.Active);

        Settle(gravity, world);

        // The whole 6-block tower cascades onto the grass eventually — exact per-cell landing
        // order under concurrent in-flight entities isn't the point of this test (real vanilla
        // has the same timing sensitivity for stacked cascades); what matters is no block is
        // lost or duplicated, the bottom cell settles, and the original top vacates.
        Assert.Equal(Blocks.Sand, world.GetBlock(2, baseY + 1, 0));
        Assert.Equal(Blocks.Air, world.GetBlock(2, baseY + 7, 0));
        var sandCount = 0;
        for (var y = baseY + 1; y <= baseY + 7; y++)
            if (world.GetBlock(2, y, 0) == Blocks.Sand) sandCount++;
        Assert.Equal(6, sandCount);
        Assert.Equal(0, world.GravityPending.Count);
        Assert.Empty(world.FallingBlocks.Active);
    }

    [Fact]
    public void Unsupported_at_void_floor_destroys()
    {
        var world = NewWorld();
        // Place sand at FlatMinY with nothing below (unsupported → void destroy).
        Assert.True(world.TrySetBlock(2, Blocks.FlatMinY, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(2, Blocks.FlatMinY, 0));

        var gravity = NewGravity(world);
        Settle(gravity, world);

        Assert.Equal(Blocks.Air, world.GetBlock(2, Blocks.FlatMinY, 0));
        Assert.Empty(world.FallingBlocks.Active);
    }

    [Fact]
    public void Gravel_placeable_and_falls_like_sand()
    {
        var world = NewWorld();
        var supportY = Blocks.FlatGrassY;
        var y = supportY + 2;
        Assert.True(world.TrySetBlock(3, y, 0, Blocks.Gravel));
        Assert.True(world.GravityPending.TryEnqueue(3, y, 0));
        var gravity = NewGravity(world);
        Settle(gravity, world);
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
    public void SettleAllPending_drains_queue_and_active_entities()
    {
        var world = NewWorld();
        Assert.True(world.TrySetBlock(4, Blocks.FlatGrassY + 3, 0, Blocks.Sand));
        Assert.True(world.GravityPending.TryEnqueue(4, Blocks.FlatGrassY + 3, 0));
        var gravity = NewGravity(world);
        // One tick to get the entity in-flight, then force-settle mid-fall (shutdown path).
        gravity.Tick(new GameClock(), Array.Empty<Zenith.Player.Player>());
        Assert.NotEmpty(world.FallingBlocks.Active);

        gravity.SettleAllPending();

        Assert.Equal(0, world.GravityPending.Count);
        Assert.Empty(world.FallingBlocks.Active);
        Assert.Equal(Blocks.Sand, world.GetBlock(4, Blocks.FlatGrassY + 1, 0));
    }
}
