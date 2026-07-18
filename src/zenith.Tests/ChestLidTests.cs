using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class ChestLidTests
{
    public ChestLidTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Opener_refcount_open_on_0_to_1_close_on_1_to_0()
    {
        var store = new ChestStore();
        Assert.True(store.TryAddOpener(1, 2, 3, 10));
        Assert.Equal(1, store.OpenerCount(1, 2, 3));

        Assert.False(store.TryAddOpener(1, 2, 3, 20));
        Assert.Equal(2, store.OpenerCount(1, 2, 3));

        Assert.False(store.TryRemoveOpener(1, 2, 3, 10));
        Assert.Equal(1, store.OpenerCount(1, 2, 3));

        Assert.True(store.TryRemoveOpener(1, 2, 3, 20));
        Assert.Equal(0, store.OpenerCount(1, 2, 3));
    }

    [Fact]
    public void TryAddOpener_same_player_is_idempotent()
    {
        var store = new ChestStore();
        Assert.True(store.TryAddOpener(0, 0, 0, 5));
        Assert.False(store.TryAddOpener(0, 0, 0, 5));
        Assert.Equal(1, store.OpenerCount(0, 0, 0));
    }

    [Fact]
    public void InventorySystem_open_then_close_drives_opener_refcount()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("opener");
        fx.World.Chests.Ensure(4, 64, 4);

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(4, 64, 4)));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.NotNull(player.OpenChest);
        Assert.Equal(OpenChestView.Single(4, 64, 4), player.OpenChest);
        Assert.Equal(1, fx.World.Chests.OpenerCount(4, 64, 4));

        Assert.True(player.SubmitWindowIntent(
            InventoryWindowIntent.Close(
                (byte)InventoryContainerMap.WindowChest,
                ContainerOpenPacket.WindowTypeChest)));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.Null(player.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(4, 64, 4));
    }

    [Fact]
    public void InventorySystem_second_viewer_keeps_lid_until_last_leaves()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        var b = fx.AddInGamePlayer("bob");
        fx.World.Chests.Ensure(2, 70, 2);

        Assert.True(a.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 70, 2)));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.Equal(1, fx.World.Chests.OpenerCount(2, 70, 2));

        Assert.True(b.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 70, 2)));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.Equal(2, fx.World.Chests.OpenerCount(2, 70, 2));

        Assert.True(a.SubmitWindowIntent(
            InventoryWindowIntent.Close(
                (byte)InventoryContainerMap.WindowChest,
                ContainerOpenPacket.WindowTypeChest)));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.Null(a.OpenChest);
        Assert.Equal(OpenChestView.Single(2, 70, 2), b.OpenChest);
        Assert.Equal(1, fx.World.Chests.OpenerCount(2, 70, 2));

        Assert.True(b.SubmitWindowIntent(
            InventoryWindowIntent.Close(
                (byte)InventoryContainerMap.WindowChest,
                ContainerOpenPacket.WindowTypeChest)));
        fx.CreateInventorySystem().Tick(fx.Clock);
        Assert.Null(b.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(2, 70, 2));
    }

    [Fact]
    public void ChestLidFanout_Open_fans_to_subject_and_peer_who_Knows()
    {
        var fx = new IntentTestFixture();
        var opener = fx.AddInGamePlayer("opener");
        var watcher = fx.AddInGamePlayer("watcher");
        opener.Chunks.RememberMany([(0, 0)]);
        watcher.Chunks.RememberMany([(0, 0)]);

        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        ChestLidFanout.Open(fx.Players.Online, opener.Session, 1, -60, 2);
        Flush(fx);

        Assert.True(fx.Transport.Captured.Count >= 2,
            $"expected ≥2 outbound frames (opener+watcher), got {fx.Transport.Captured.Count}");
    }

    [Fact]
    public void ChestLidFanout_skips_peer_without_Knows()
    {
        var fx = new IntentTestFixture();
        var opener = fx.AddInGamePlayer("opener");
        _ = fx.AddInGamePlayer("far");
        opener.Chunks.RememberMany([(0, 0)]);
        // peer has no Knows — should not receive lid event

        Flush(fx);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        ChestLidFanout.Open(fx.Players.Online, opener.Session, 1, -60, 2);
        Flush(fx);

        Assert.Single(fx.Transport.Captured);
    }

    private static void Flush(IntentTestFixture fx)
    {
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();
    }
}
