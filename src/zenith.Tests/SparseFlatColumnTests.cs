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
        world.PersistDirtyColumns(int.MaxValue);

        Assert.Equal(0, storage.PutCount);
        Assert.Null(await storage.GetColumnAsync(new ChunkCoord(0, 0)));
    }

    [Fact]
    public async Task A_stored_column_that_cannot_be_read_is_regenerated_instead_of_failing_the_load()
    {
        var storage = new InMemoryChunkStorage();
        await storage.PutColumnAsync(new ChunkCoord(2, 3), [1, 2, 3]);

        var world = new World.World(storage);
        var column = await world.GetOrCreateColumnAsync(2, 3);

        Assert.True(column.GetWire().SubChunkCount > 0);
        Assert.Equal(SubChunkWire.Version, column.GetWire().Payload[0]);
        Assert.False(column.IsDirty); // the damaged blob is replaced the first time the column is edited and saved
    }

    [Fact]
    public async Task An_edited_column_is_saved_once_and_a_second_save_with_no_edit_writes_nothing()
    {
        var storage = new InMemoryChunkStorage();
        var world = new World.World(storage);
        world.SetBlock(1, 64, 2, Blocks.Stone);

        Assert.Equal(1, world.PersistDirtyColumns(int.MaxValue));
        Assert.Equal(1, storage.PutCount);
        Assert.Equal(0, world.PersistDirtyColumns(int.MaxValue));
        Assert.Equal(1, storage.PutCount);

        var reloaded = new World.World(storage);
        _ = await reloaded.GetOrCreateColumnAsync(0, 0);
        Assert.Equal(Blocks.Stone, reloaded.GetBlock(1, 64, 2));
        Assert.Equal(0, reloaded.SyncLoadCount);
    }
}
