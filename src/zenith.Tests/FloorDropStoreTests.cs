using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class FloorDropStoreTests
{
    public FloorDropStoreTests() => Blocks.EnsureLoaded();

    [Fact]
    public void TryAddOrMerge_refuses_different_id_same_cell()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(1, 64, 1, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: 1, out _));
        Assert.False(store.TryAddOrMerge(1, 64, 1, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 2, out _));
        Assert.Equal(1, store.Count);
        Assert.True(store.TryTake(1, 64, 1, out var id, out var count, out _));
        Assert.Equal(StackId.FromBlock(Blocks.Stone), id);
        Assert.Equal(1, count);
    }

    [Fact]
    public void TryAddOrMerge_refuses_new_cell_at_soft_cap()
    {
        var store = new FloorDropStore();
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(store.TryAddOrMerge(i, 64, 0, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: i + 1, out _));

        Assert.Equal(FloorDropStore.SoftCap, store.Count);
        Assert.False(store.TryAddOrMerge(FloorDropStore.SoftCap, 64, 0, StackId.FromBlock(Blocks.Stone), 1, 99999, out _));
        Assert.Equal(FloorDropStore.SoftCap, store.Count);
    }

    [Fact]
    public void TryAddOrMerge_allows_merge_when_at_soft_cap()
    {
        var store = new FloorDropStore();
        for (var i = 0; i < FloorDropStore.SoftCap; i++)
            Assert.True(store.TryAddOrMerge(i, 64, 0, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: i + 1, out _));

        Assert.True(store.TryAddOrMerge(0, 64, 0, StackId.FromBlock(Blocks.Stone), 1, 99999, out var dep));
        Assert.NotNull(dep);
        Assert.False(dep!.Value.Created);
        Assert.True(dep.Value.CountChanged);
        Assert.Equal(1, dep.Value.EntityRuntimeId);
        Assert.Equal(FloorDropStore.SoftCap, store.Count);
        Assert.True(store.TryTake(0, 64, 0, out var rid, out var count, out var eid));
        Assert.Equal(StackId.FromBlock(Blocks.Stone), rid);
        Assert.Equal(2, count);
        Assert.Equal(1, eid);
    }

    [Fact]
    public void TryAddOrMerge_refuses_overflow_without_losing_or_mutating_items()
    {
        var store = new FloorDropStore();
        var dirt = StackId.FromBlock(Blocks.Dirt);
        Assert.True(store.TryAddOrMerge(2, 64, 2, dirt, 64, entityRuntimeIdIfNew: 1, out _));

        Assert.False(store.TryAddOrMerge(2, 64, 2, dirt, 1, entityRuntimeIdIfNew: 2, out _));
        Assert.False(store.TryAddOrMerge(3, 64, 2, dirt, 65, entityRuntimeIdIfNew: 3, out _));

        Assert.Equal(1, store.Count);
        Assert.True(store.TryTake(2, 64, 2, out var id, out var count, out _));
        Assert.Equal(dirt, id);
        Assert.Equal(64, count);
    }

    [Fact]
    public void TryAddOrMerge_new_cell_keeps_allocated_entity_id()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(1, 64, 2, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 77, out var dep));
        Assert.NotNull(dep);
        Assert.True(dep!.Value.Created);
        Assert.Equal(77, dep.Value.EntityRuntimeId);
        Assert.Equal(StackId.FromBlock(Blocks.Dirt), dep.Value.Id);
    }

    [Fact]
    public void TryTakeUpTo_partial_leaves_remaining_for_republish()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(3, 64, 3, StackId.FromBlock(Blocks.Dirt), 5, entityRuntimeIdIfNew: 42, out _));

        Assert.True(store.TryTakeUpTo(
            3, 64, 3, max: 1,
            out var rid, out var taken, out var eid, out var rem));
        Assert.Equal(StackId.FromBlock(Blocks.Dirt), rid);
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
        Assert.True(store.TryAddOrMerge(4, 64, 4, StackId.FromBlock(Blocks.Stone), 3, entityRuntimeIdIfNew: 9, out _));
        Assert.True(store.TryTakeUpTo(4, 64, 4, 3, out _, out var taken, out _, out var rem));
        Assert.Equal(3, taken);
        Assert.Null(rem);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void TryAddOrMerge_default_delay_then_tick_to_zero()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(1, 64, 1, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 1, out _));
        var snap = store.Snapshot().Single();
        Assert.Equal(FloorDropStore.DefaultPickupDelay, snap.PickupDelayTicks);

        store.TickPickupDelays(FloorDropStore.DefaultPickupDelay);
        snap = store.Snapshot().Single();
        Assert.Equal(0, snap.PickupDelayTicks);
    }

    [Fact]
    public void TryAddOrMerge_merge_uses_max_pickup_delay()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(
            2, 64, 2, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 1, out _, pickupDelayTicks: 0));
        Assert.True(store.TryAddOrMerge(
            2, 64, 2, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 99, out _, pickupDelayTicks: 10));
        var snap = store.Snapshot().Single();
        Assert.Equal(10, snap.PickupDelayTicks);
        Assert.Equal(2, snap.Count);
    }

    [Fact]
    public void TryTakeUpTo_partial_preserves_pickup_delay()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(
            3, 64, 3, StackId.FromBlock(Blocks.Dirt), 5, entityRuntimeIdIfNew: 42, out _, pickupDelayTicks: 7));
        Assert.True(store.TryTakeUpTo(3, 64, 3, 1, out _, out _, out _, out _));
        var snap = store.Snapshot().Single();
        Assert.Equal(7, snap.PickupDelayTicks);
        Assert.Equal(4, snap.Count);
    }

    [Fact]
    public void TickDespawn_below_threshold_keeps_cell_and_ages_it()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(1, 64, 1, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 1, out _));

        var expired = new List<(int X, int Y, int Z, StackId Id, int Count, long EntityRuntimeId)>();
        store.TickDespawn(expired, tickDiff: FloorDropStore.DefaultDespawnTicks - 1);

        Assert.Empty(expired);
        Assert.Equal(1, store.Count);
        Assert.Equal(FloorDropStore.DefaultDespawnTicks - 1, store.Snapshot().Single().AgeTicks);
    }

    [Fact]
    public void TickDespawn_at_threshold_removes_cell_and_reports_it()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(2, 64, 2, StackId.FromBlock(Blocks.Stone), 3, entityRuntimeIdIfNew: 7, out _));

        var expired = new List<(int X, int Y, int Z, StackId Id, int Count, long EntityRuntimeId)>();
        store.TickDespawn(expired, tickDiff: FloorDropStore.DefaultDespawnTicks);

        Assert.Equal(0, store.Count);
        var e = Assert.Single(expired);
        Assert.Equal((2, 64, 2), (e.X, e.Y, e.Z));
        Assert.Equal(StackId.FromBlock(Blocks.Stone), e.Id);
        Assert.Equal(3, e.Count);
        Assert.Equal(7, e.EntityRuntimeId);
    }

    [Fact]
    public void TickDespawn_merge_does_not_reset_age()
    {
        var store = new FloorDropStore();
        Assert.True(store.TryAddOrMerge(4, 64, 4, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 1, out _));

        var expired = new List<(int X, int Y, int Z, StackId Id, int Count, long EntityRuntimeId)>();
        store.TickDespawn(expired, tickDiff: FloorDropStore.DefaultDespawnTicks - 1);
        Assert.Empty(expired);

        // Topping off the pile one tick before despawn must not grant it a fresh lifetime.
        Assert.True(store.TryAddOrMerge(4, 64, 4, StackId.FromBlock(Blocks.Dirt), 1, entityRuntimeIdIfNew: 2, out _));
        store.TickDespawn(expired, tickDiff: 1);

        Assert.Equal(0, store.Count);
        Assert.Single(expired);
    }
}
