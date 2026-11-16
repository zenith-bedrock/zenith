using Zenith.Session;
using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// ADR §114/§167 — World.TryEvictChunk / ChunkResidencySystem's sweep target. Exercised directly against World (no
/// GameLoop or ChunkStreamSystem): the contract (skip when viewed or while a container is open, save what was edited,
/// forget what was not, reload identically on the next touch) does not depend on how residency gets acquired.
/// </summary>
public sealed class ChunkResidencyEvictionTests
{
    public ChunkResidencyEvictionTests() => Blocks.EnsureLoaded();

    [Fact]
    public async Task TryEvictChunk_returns_false_and_changes_nothing_while_a_viewer_is_present()
    {
        var world = new World.World(new InMemoryChunkStorage());
        world.SetBlock(1, 64, 2, Blocks.Stone);
        await world.GetOrCreateColumnAsync(0, 0);

        world.ChunkResidency.Acquire(0, 0);
        Assert.False(world.TryEvictChunk(0, 0));
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));
        Assert.Equal(1, world.LoadedColumnCount);
    }

    [Fact]
    public async Task An_evicted_edited_column_comes_back_identical_on_the_next_touch()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        world.SetBlock(1, 64, 2, Blocks.Stone);
        world.Chests.Ensure(3, 64, 4);
        Assert.True(world.Chests.TrySet(3, 64, 4, 0, InventorySlot.OfBlock(Blocks.Dirt, 5)));

        Assert.True(world.TryEvictChunk(0, 0));
        Assert.Equal(0, world.LoadedColumnCount);

        await world.GetOrCreateColumnAsync(0, 0);
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));
        Assert.True(world.Chests.TryGetSlots(3, 64, 4, out var slots));
        Assert.Equal(Blocks.Dirt, slots[0].Id.Value);
        Assert.Equal(5, slots[0].Count);
    }

    [Fact]
    public async Task An_untouched_column_is_forgotten_without_being_written()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        await world.GetOrCreateColumnAsync(0, 0);

        Assert.True(world.TryEvictChunk(0, 0));

        Assert.Equal(0, storage.PutCount);
        Assert.Equal(0, world.LoadedColumnCount);
    }

    [Fact]
    public async Task TryEvictChunk_leaves_a_column_with_an_open_container_resident()
    {
        var world = new World.World(new InMemoryChunkStorage());
        await world.GetOrCreateColumnAsync(0, 0);
        world.Chests.Ensure(1, 64, 2);
        Assert.True(world.Chests.TryAddOpener(1, 64, 2, playerRuntimeId: 42));

        Assert.False(world.TryEvictChunk(0, 0));
        Assert.Equal(1, world.LoadedColumnCount);
        Assert.Equal(1, world.Chests.OpenerCount(1, 64, 2));
    }

    [Fact]
    public async Task CopyLoadedColumns_no_longer_lists_an_evicted_chunk()
    {
        var world = new World.World(new InMemoryChunkStorage());
        await world.GetOrCreateColumnAsync(0, 0);
        Assert.Contains((0, 0), world.CopyLoadedColumns());

        world.TryEvictChunk(0, 0);
        Assert.DoesNotContain((0, 0), world.CopyLoadedColumns());
    }

    [Fact]
    public void A_cell_in_a_chunk_nobody_streamed_is_loaded_on_demand_and_counted()
    {
        var world = new World.World(new InMemoryChunkStorage());
        Assert.Equal(0, world.SyncLoadCount);

        Assert.True(world.TrySetBlock(1, 64, 2, Blocks.Stone));

        Assert.Equal(1, world.SyncLoadCount);
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));
        Assert.Equal(1, world.SyncLoadCount);
    }

    [Fact]
    public void Ticking_systems_never_wake_an_unloaded_chunk()
    {
        var world = new World.World(new InMemoryChunkStorage());

        Assert.False(world.TryGetBlock(1, 64, 2, out _));
        Assert.Equal(0, world.LoadedColumnCount);
    }
}
