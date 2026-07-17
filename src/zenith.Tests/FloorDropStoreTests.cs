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

    [Fact]
    public void TryTakeUpTo_partial_leaves_remaining_for_republish()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(3, 64, 3, Blocks.Dirt, 5, entityRuntimeIdIfNew: 42, out _));

        Assert.True(store.TryTakeUpTo(
            3, 64, 3, max: 1,
            out var rid, out var taken, out var eid, out var rem));
        Assert.Equal(Blocks.Dirt, rid);
        Assert.Equal(1, taken);
        Assert.Equal(42, eid);
        Assert.NotNull(rem);
        Assert.True(rem!.Value.CountChanged);
        Assert.Equal(4, rem.Value.Count);
        Assert.Equal(1, store.Count);

        Assert.True(store.TryTake(3, 64, 3, out _, out var left, out _));
        Assert.Equal(4, left);
    }

    [Fact]
    public void TryTakeUpTo_full_removes_cell()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(4, 64, 4, Blocks.Stone, 3, entityRuntimeIdIfNew: 9, out _));
        Assert.True(store.TryTakeUpTo(4, 64, 4, 3, out _, out var taken, out _, out var rem));
        Assert.Equal(3, taken);
        Assert.Null(rem);
        Assert.Equal(0, store.Count);
    }
}
