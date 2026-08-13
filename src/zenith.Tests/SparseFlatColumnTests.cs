using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class SparseFlatColumnTests
{
    public SparseFlatColumnTests() => Blocks.EnsureLoaded();

    [Fact]
    public async Task Virgin_miss_does_not_Put_column()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);

        for (var i = 0; i < 5; i++)
            _ = await world.GetOrCreateColumnAsync(i, i);

        Assert.Equal(0, storage.PutCount);
        Assert.Null(await storage.GetAsync(new ChunkCoord(0, 0)));
    }

    [Fact]
    public async Task Legacy_empty_payload_still_migrates_with_Put()
    {
        var storage = new InMemoryChunkStorage();
        // Biome-only empty header byte 1 — not LooksLikeTerrainPayload (wants ChunkPayloads.SubChunkVersion).
        var empty = new ChunkColumnData(new ChunkCoord(2, 3), 0, subChunkCount: 1, extraPayload: [1, 2, 3]);
        await storage.PutAsync(empty);
        var putsBefore = storage.PutCount;

        var world = new World.World(storage);
        var column = await world.GetOrCreateColumnAsync(2, 3);

        Assert.True(storage.PutCount > putsBefore);
        Assert.True(column.Base.SubChunkCount > 0);
        Assert.Equal(ChunkPayloads.SubChunkVersion, column.Base.ExtraPayload[0]);
    }

    [Fact]
    public async Task Terrain_column_reused_without_extra_Put()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        // Force migrate once by planting legacy empty, then reuse.
        await storage.PutAsync(new ChunkColumnData(new ChunkCoord(0, 0), 0, 1, [1]));
        _ = await world.GetOrCreateColumnAsync(0, 0);
        var afterMigrate = storage.PutCount;

        _ = await world.GetOrCreateColumnAsync(0, 0);
        Assert.Equal(afterMigrate, storage.PutCount);
    }
}
