using Zenith.Gameplay.Inventory;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// ChestStore's SoftCap (§36) refuses only brand-new cells made by live gameplay. Hydrate from disk must still load every saved
/// chest, and a refused chest window must not open at all, so no path shows a chest that would silently reject every move.
/// </summary>
public class ChestStoreSoftCapTests
{
    public ChestStoreSoftCapTests() => Blocks.EnsureLoaded();

    private static ChestStore FullStore()
    {
        var store = new ChestStore();
        for (var i = 0; i < ChestStore.SoftCap; i++)
            Assert.True(store.TryEnsure(i, 64, 0));
        return store;
    }

    [Fact]
    public void TryEnsure_refuses_a_new_cell_at_the_cap_but_accepts_an_existing_one()
    {
        var store = FullStore();

        Assert.False(store.TryEnsure(0, 64, 5));
        Assert.True(store.TryEnsure(0, 64, 0));
    }

    [Fact]
    public void Hydrate_loads_a_saved_chest_even_when_the_store_is_at_the_cap()
    {
        var source = new ChestStore();
        Assert.True(source.TryEnsure(5, 64, 5));
        var stack = StackId.FromItem(Blocks.Dirt);
        Assert.True(source.TrySet(5, 64, 5, 0, new InventorySlot(stack, 7)));
        var blob = source.PackBlob(5, 64, 5)!;

        var store = FullStore();

        Assert.True(store.TryLoadFromBlob(5, 64, 5, blob));
        Assert.Equal(7, store.Get(5, 64, 5, 0).Count);
        Assert.Equal(ChestStore.SoftCap + 1, store.Count);
    }

    [Fact]
    public void Chest_window_is_refused_when_its_cell_cannot_be_created()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("opener");
        for (var i = 0; i < ChestStore.SoftCap; i++)
            Assert.True(fx.World.Chests.TryEnsure(i, 70, 0));

        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(4, 64, 4)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Null(player.Windows.OpenChest);
        Assert.False(fx.World.Chests.CanAcceptNew(4, 64, 4));
        Assert.Equal(0, fx.World.Chests.OpenerCount(4, 64, 4));
    }

    [Fact]
    public void World_SetBlock_throws_instead_of_dropping_an_edit_at_the_overlay_cap()
    {
        var fx = new IntentTestFixture();
        var world = fx.World;
        for (var i = 0; i < World.World.OverrideSoftCap; i++)
            world.SetBlock(i, 100, 0, Blocks.Stone);

        Assert.False(world.TrySetBlock(0, 101, 0, Blocks.Stone));
        Assert.Throws<InvalidOperationException>(() => world.SetBlock(0, 101, 0, Blocks.Stone));
    }
}
