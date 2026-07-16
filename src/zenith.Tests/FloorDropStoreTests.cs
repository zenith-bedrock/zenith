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
            Assert.True(store.TryAddOrMerge(i, 64, 0, Blocks.Stone, 1, entityRuntimeIdIfNew: i + 1, out _));

        Assert.Equal(FloorDropStore.SoftCap, store.Count);
        Assert.False(store.TryAddOrMerge(FloorDropStore.SoftCap, 64, 0, Blocks.Stone, 1, 99999, out _));
        Assert.Equal(FloorDropStore.SoftCap, store.Count);
    }

    [Fact]
    public void TryAddOrMerge_allows_merge_when_at_soft_cap()
    {
        var store = new FloorDropStore();
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(store.TryAddOrMerge(i, 64, 0, Blocks.Stone, 1, entityRuntimeIdIfNew: i + 1, out _));

        Assert.True(store.TryAddOrMerge(0, 64, 0, Blocks.Stone, 1, 99999, out var dep));
        Assert.NotNull(dep);
        Assert.False(dep!.Value.Created);
        Assert.True(dep.Value.CountChanged);
        Assert.Equal(1, dep.Value.EntityRuntimeId);
        Assert.Equal(FloorDropStore.SoftCap, store.Count);
        Assert.True(store.TryTake(0, 64, 0, out var rid, out var count, out var eid));
        Assert.Equal(Blocks.Stone, rid);
        Assert.Equal(2, count);
        Assert.Equal(1, eid);
    }

    [Fact]
    public void TryAddOrMerge_new_cell_keeps_allocated_entity_id()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(1, 64, 2, Blocks.Dirt, 1, entityRuntimeIdIfNew: 77, out var dep));
        Assert.NotNull(dep);
        Assert.True(dep!.Value.Created);
        Assert.Equal(77, dep.Value.EntityRuntimeId);
        Assert.Equal(Blocks.Dirt, dep.Value.ItemRuntimeId);
    }
}
