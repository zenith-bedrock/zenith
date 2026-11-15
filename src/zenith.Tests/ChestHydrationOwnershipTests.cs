using Zenith.Player;
using Zenith.Session;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class ChestHydrationOwnershipTests
{
    [Fact]
    public async Task Parallel_cold_reads_publish_blobs_without_writing_live_chests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zenith-chest-hydration-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var storage = new LevelDbChunkStorage(dir);
            var slots = new InventorySlot[ChestStore.SingleSize]; slots[0] = InventorySlot.OfBlock(Blocks.Dirt, 5);
            for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++) await storage.PutChestAsync(x * 16 + 1, 64, z * 16 + 1, SlotBlob.Pack(slots));
            await storage.FlushAsync();
            var world = new World.World(storage, generationWorkers: 4);
            try
            {
                var columns = await world.GetRadiusAsync(0, 0, 2);
                Assert.Equal(25, columns.Count);
                Assert.Equal(0, world.Chests.Count); // I/O workers must never touch the live store.
                foreach (var column in columns) world.ApplyChunkHydration(column.Base.Coord.X, column.Base.Coord.Z);
                Assert.Equal(25, world.Chests.Count);
                Assert.Equal(5, world.Chests.Get(1, 64, 1, 0).Count);
                Assert.True(world.Chests.TrySet(1, 64, 1, 0, InventorySlot.OfBlock(Blocks.Stone, 7)));
                world.ApplyChunkHydration(0, 0); // Re-applying may not resurrect stale contents.
                Assert.Equal(InventorySlot.OfBlock(Blocks.Stone, 7), world.Chests.Get(1, 64, 1, 0));
            }
            finally { await world.StopGenerationAsync(); }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Pending_seed_survives_a_cached_second_read_and_preserves_live_edits()
    {
        var storage = new InMemoryChunkStorage();
        var slots = new InventorySlot[ChestStore.SingleSize]; slots[0] = InventorySlot.OfBlock(Blocks.Dirt, 5);
        await storage.PutChestAsync(1, 64, 1, SlotBlob.Pack(slots));
        var world = new World.World(storage);
        try
        {
            await world.GetOrCreateColumnAsync(0, 0); // First viewer can leave without publication.
            await world.GetOrCreateColumnAsync(0, 0); // Cache hit must still have the pending seed.
            Assert.Equal(0, world.Chests.Count);
            Assert.True(world.Chests.TryEnsure(1, 64, 1));
            Assert.True(world.Chests.TrySet(1, 64, 1, 0, InventorySlot.OfBlock(Blocks.Stone, 7)));
            world.ApplyChunkHydration(0, 0);
            Assert.Equal(InventorySlot.OfBlock(Blocks.Stone, 7), world.Chests.Get(1, 64, 1, 0));
        }
        finally { await world.StopGenerationAsync(); }
    }

    [Fact]
    public async Task Post_spawn_tick_applies_pending_chests_before_publishing_the_column()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("hydrated-viewer");
        player.Chunks.Radius = -1;
        fx.World.Chests.TryEnsure(1, 64, 1);
        fx.World.Chests.TrySet(1, 64, 1, 0, InventorySlot.OfBlock(Blocks.Dirt, 5));
        fx.World.PersistChest(1, 64, 1); await fx.World.FlushPersistenceAsync();
        Assert.True(fx.World.TryEvictChunk(0, 0));
        try
        {
            var column = await fx.World.GetOrCreateColumnAsync(0, 0);
            Assert.Equal(0, fx.World.Chests.Count);
            Assert.True(player.Chunks.TryBegin(0, 0, out var epoch));
            player.Chunks.CompleteStream(0, 0, epoch, column);
            new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
            Assert.Equal(5, fx.World.Chests.Get(1, 64, 1, 0).Count);
        }
        finally { await fx.World.StopGenerationAsync(); }
    }
}
