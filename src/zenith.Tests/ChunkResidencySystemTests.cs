using Xunit;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.World;

namespace Zenith.Tests;

public sealed class ChunkResidencySystemTests
{
    public ChunkResidencySystemTests() => Blocks.EnsureLoaded();

    [Fact]
    public async Task Tick_evicts_an_unviewed_hydrated_chunk_on_the_sweep_tick()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        world.SetBlock(1, 64, 2, Blocks.Stone);
        await world.GetOrCreateColumnAsync(0, 0);
        Assert.Contains((0, 0), world.CopyHydratedChunks());

        var system = new ChunkResidencySystem(world, sweepIntervalTicks: 10);
        var clock = new GameClock();
        clock.AdvanceBy(10); // lands exactly on the sweep tick

        system.Tick(clock, []);

        Assert.DoesNotContain((0, 0), world.CopyHydratedChunks());
    }

    [Fact]
    public async Task Tick_does_not_evict_off_the_sweep_interval()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        world.SetBlock(1, 64, 2, Blocks.Stone);
        await world.GetOrCreateColumnAsync(0, 0);

        var system = new ChunkResidencySystem(world, sweepIntervalTicks: 10);
        var clock = new GameClock();
        clock.AdvanceBy(5); // not a multiple of 10

        system.Tick(clock, []);

        Assert.Contains((0, 0), world.CopyHydratedChunks());
    }

    [Fact]
    public async Task Tick_never_evicts_a_chunk_with_an_active_viewer()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        world.SetBlock(1, 64, 2, Blocks.Stone);
        await world.GetOrCreateColumnAsync(0, 0);
        world.ChunkResidency.Acquire(0, 0);

        var system = new ChunkResidencySystem(world, sweepIntervalTicks: 10);
        var clock = new GameClock();
        clock.AdvanceBy(10);

        system.Tick(clock, []);

        Assert.Contains((0, 0), world.CopyHydratedChunks());
        Assert.Equal(Blocks.Stone, world.GetBlock(1, 64, 2));
    }
}
