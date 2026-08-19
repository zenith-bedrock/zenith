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

    /// <summary>
    /// A GameLoop-tick edit (e.g. an explosion's block break) has no chunk-hydration gate — it can
    /// target a cell in a chunk no player has ever streamed. If that chunk's first hydrate is
    /// concurrently racing to read a stale, previously-persisted overlay for that same cell,
    /// SeedOverlayIfAbsent's TryAdd must make the live edit win — an overwrite here would silently
    /// revert a just-made edit back to old disk data the instant hydrate finished.
    /// </summary>
    [Fact]
    public async Task A_live_edit_racing_ahead_of_first_hydrate_is_not_overwritten_by_stale_persisted_data()
    {
        var storage = new InMemoryChunkStorage();
        // Simulates a value persisted in an earlier server run, before this chunk has ever been
        // touched (hydrated) in this one.
        await storage.PutOverlayAsync(1, 64, 2, Blocks.Dirt);

        var world = new World.World(storage);
        // Live edit lands first — chunk (0,0) has not been requested/hydrated yet at this point.
        Assert.True(world.TrySetBlock(1, 64, 2, Blocks.Stone));
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));

        // First hydrate for this chunk now runs and loads the stale persisted overlay.
        await world.GetOrCreateColumnAsync(0, 0);

        // The live edit must still win — hydrate seeds gaps, it never overwrites live RAM state.
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));
    }

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
