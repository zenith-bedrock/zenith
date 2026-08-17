using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// ADR §114 — World.TryEvictChunk / ChunkResidencySystem's sweep target. These tests exercise
/// eviction directly against World (no GameLoop/ChunkStreamSystem involved) since TryEvictChunk's
/// contract (skip when viewed, persist-then-evict chests, drop overlays, survive rehydrate) is
/// independent of how residency gets acquired/released.
/// </summary>
public sealed class ChunkResidencyEvictionTests
{
    public ChunkResidencyEvictionTests() => Blocks.EnsureLoaded();

    [Fact]
    public async Task TryEvictChunk_returns_false_and_changes_nothing_while_a_viewer_is_present()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        world.SetBlock(1, 64, 2, Blocks.Stone);
        await world.GetOrCreateColumnAsync(0, 0);

        world.ChunkResidency.Acquire(0, 0);
        Assert.False(world.TryEvictChunk(0, 0));
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));
    }

    [Fact]
    public async Task TryEvictChunk_drops_overlays_and_a_later_touch_rehydrates_them_identically()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        world.SetBlock(1, 64, 2, Blocks.Stone);
        await world.GetOrCreateColumnAsync(0, 0);
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));

        Assert.True(world.TryEvictChunk(0, 0));
        // Evicted: RAM copy gone, base terrain shows through until rehydrate.
        Assert.NotEqual(Blocks.Stone, world.GetBlock(1, 64, 2));

        await world.GetOrCreateColumnAsync(0, 0); // touch again → rehydrate from storage
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));
    }

    [Fact]
    public async Task TryEvictChunk_persists_and_evicts_a_chest_with_no_opener()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        await world.GetOrCreateColumnAsync(0, 0);
        world.Chests.Ensure(1, 64, 2);
        Assert.True(world.Chests.TrySet(1, 64, 2, 0, InventorySlot.OfBlock(Blocks.Dirt, 5)));

        Assert.True(world.TryEvictChunk(0, 0));
        Assert.False(world.Chests.TryGetSlots(1, 64, 2, out _));

        await world.GetOrCreateColumnAsync(0, 0); // rehydrate
        Assert.True(world.Chests.TryGetSlots(1, 64, 2, out var slots));
        Assert.Equal(Blocks.Dirt, slots[0].Id.Value);
        Assert.Equal(5, slots[0].Count);
    }

    [Fact]
    public async Task TryEvictChunk_leaves_a_chest_with_an_open_UI_resident()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        await world.GetOrCreateColumnAsync(0, 0);
        world.Chests.Ensure(1, 64, 2);
        Assert.True(world.Chests.TryAddOpener(1, 64, 2, playerRuntimeId: 42));

        Assert.True(world.TryEvictChunk(0, 0)); // chunk itself still evicts (no chunk viewer)
        Assert.True(world.Chests.TryGetSlots(1, 64, 2, out _)); // but this chest stayed resident
    }

    [Fact]
    public async Task CopyHydratedChunks_no_longer_lists_an_evicted_chunk()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        await world.GetOrCreateColumnAsync(0, 0);
        Assert.Contains((0, 0), world.CopyHydratedChunks());

        world.TryEvictChunk(0, 0);
        Assert.DoesNotContain((0, 0), world.CopyHydratedChunks());
    }
}
