using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

public class BlockEditIntentTests
{
    [Fact]
    public void IsInWorldBounds_rejects_extreme_coords()
    {
        Assert.False(BlockEditIntent.Set(40_000_000, 64, 0, 1).IsInWorldBounds());
        Assert.False(BlockEditIntent.Set(0, 9999, 0, 1).IsInWorldBounds());
        Assert.True(BlockEditIntent.Set(0, 64, 0, 1).IsInWorldBounds());
    }
}

public class InMemoryChunkStorageTests
{
    [Fact]
    public async Task Put_then_Get_returns_same_column()
    {
        var storage = new InMemoryChunkStorage();
        var column = new ChunkColumnData(new ChunkCoord(1, 2), 0, 0, [1, 2, 3]);
        await storage.PutAsync(column);
        var got = await storage.GetAsync(new ChunkCoord(1, 2));
        Assert.NotNull(got);
        Assert.Equal(column.Coord, got!.Coord);
        Assert.Equal(column.ExtraPayload, got.ExtraPayload);
    }
}
