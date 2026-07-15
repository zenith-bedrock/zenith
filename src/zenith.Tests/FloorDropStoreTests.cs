using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class FloorDropStoreTests
{
    public FloorDropStoreTests() => Blocks.EnsureLoaded();

    [Fact]
    public void TryAddOrMerge_refuses_new_cell_at_soft_cap()
    {
        var store = new FloorDropStore();
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(store.TryAddOrMerge(i, 64, 0, Blocks.Stone, 1));

        Assert.Equal(FloorDropStore.SoftCap, store.Count);
        Assert.False(store.TryAddOrMerge(FloorDropStore.SoftCap, 64, 0, Blocks.Stone, 1));
        Assert.Equal(FloorDropStore.SoftCap, store.Count);
    }

    [Fact]
    public void TryAddOrMerge_allows_merge_when_at_soft_cap()
    {
        var store = new FloorDropStore();
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(store.TryAddOrMerge(i, 64, 0, Blocks.Stone, 1));

        Assert.True(store.TryAddOrMerge(0, 64, 0, Blocks.Stone, 1));
        Assert.Equal(FloorDropStore.SoftCap, store.Count);
        Assert.True(store.TryTake(0, 64, 0, out var rid, out var count));
        Assert.Equal(Blocks.Stone, rid);
        Assert.Equal(2, count);
    }
}
