using Xunit;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>ADR §95 — mirrors GravityPendingStore/FloorDropStore's SoftCap-refuse-and-warn-once discipline.</summary>
public class FallingBlockStoreTests
{
    private static FallingBlockEntry MakeEntry(long id) => new()
    {
        EntityId = id,
        RuntimeId = (ulong)id,
        BlockRuntimeId = 1,
        LandX = 0,
        LandY = 0,
        LandZ = 0
    };

    [Fact]
    public void TrySpawn_adds_entries_below_soft_cap()
    {
        var store = new FallingBlockStore();
        Assert.True(store.TrySpawn(MakeEntry(1)));
        Assert.Single(store.Active);
    }

    [Fact]
    public void Remove_takes_an_entry_back_out_of_active()
    {
        var store = new FallingBlockStore();
        var entry = MakeEntry(1);
        Assert.True(store.TrySpawn(entry));

        store.Remove(entry);

        Assert.Empty(store.Active);
    }

    [Fact]
    public void TrySpawn_refuses_once_the_soft_cap_is_reached()
    {
        var store = new FallingBlockStore();
        for (var i = 0; i < FallingBlockStore.SoftCap; i++)
            Assert.True(store.TrySpawn(MakeEntry(i)));

        Assert.False(store.TrySpawn(MakeEntry(FallingBlockStore.SoftCap)));
        Assert.Equal(FallingBlockStore.SoftCap, store.Active.Count);
    }

    [Fact]
    public void TrySpawn_accepts_again_after_a_slot_frees_up_past_the_soft_cap()
    {
        var store = new FallingBlockStore();
        var entries = new List<FallingBlockEntry>();
        for (var i = 0; i < FallingBlockStore.SoftCap; i++)
        {
            var entry = MakeEntry(i);
            entries.Add(entry);
            Assert.True(store.TrySpawn(entry));
        }
        Assert.False(store.TrySpawn(MakeEntry(FallingBlockStore.SoftCap)));

        store.Remove(entries[0]);

        Assert.True(store.TrySpawn(MakeEntry(FallingBlockStore.SoftCap + 1)));
        Assert.Equal(FallingBlockStore.SoftCap, store.Active.Count);
    }
}
