using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Replication;

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
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.OpenChest);
        Assert.Equal(OpenChestView.Single(4, 64, 4), player.OpenChest);
        Assert.Equal(1, fx.World.Chests.OpenerCount(4, 64, 4));

        Assert.True(player.SubmitWindowIntent(
            InventoryWindowIntent.Close(
                (byte)InventoryContainerMap.WindowChest,
                ContainerOpenPacket.WindowTypeChest)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
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
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, fx.World.Chests.OpenerCount(2, 70, 2));

        Assert.True(b.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 70, 2)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, fx.World.Chests.OpenerCount(2, 70, 2));

        Assert.True(a.SubmitWindowIntent(
            InventoryWindowIntent.Close(
                (byte)InventoryContainerMap.WindowChest,
                ContainerOpenPacket.WindowTypeChest)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Null(a.OpenChest);
        Assert.Equal(OpenChestView.Single(2, 70, 2), b.OpenChest);
        Assert.Equal(1, fx.World.Chests.OpenerCount(2, 70, 2));

        Assert.True(b.SubmitWindowIntent(
            InventoryWindowIntent.Close(
                (byte)InventoryContainerMap.WindowChest,
                ContainerOpenPacket.WindowTypeChest)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Null(b.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(2, 70, 2));
    }

    /// <summary>Phase XXIII-B — real-client report: a container never opens again after the first close.</summary>
    [Fact]
    public void InventorySystem_can_reopen_the_same_chest_after_closing_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("reopener");
        fx.World.Chests.Ensure(4, 64, 4);

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(4, 64, 4)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.OpenChest);

        Assert.True(player.SubmitWindowIntent(
            InventoryWindowIntent.Close((byte)InventoryContainerMap.WindowChest, ContainerOpenPacket.WindowTypeChest)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.OpenChest);

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(4, 64, 4)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.OpenChest);
        Assert.Equal(OpenChestView.Single(4, 64, 4), player.OpenChest);
    }

    /// <summary>Real Bedrock clients sometimes send WindowId 0xFF ("none") instead of the actual id on close — PocketMine documents this since 1.21.100. Must not permanently lock the player out of reopening.</summary>
    [Fact]
    public void InventorySystem_closes_the_active_chest_even_when_the_client_sends_the_unknown_window_id_quirk()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("quirky-close");
        fx.World.Chests.Ensure(4, 64, 4);

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(4, 64, 4)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.OpenChest);

        Assert.True(player.SubmitWindowIntent(
            InventoryWindowIntent.Close(InventoryWindowIntent.UnknownWindowId, InventoryWindowIntent.UnknownWindowId)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(4, 64, 4));

        // Must still be able to reopen afterward — the actual reported symptom.
        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(4, 64, 4)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.OpenChest);
    }

    /// <summary>
    /// Phase XXVI — real-run log evidence: a client sent Close(WindowId=2, WindowType=247) against an
    /// active chest session of (WindowId=2, WindowType=0/Chest). WindowId matched exactly; only the
    /// otherwise-unvalidated WindowType field differed. The strict WindowType comparison rejected this
    /// as "mismatched," leaving the session open server-side while the client believed it had closed —
    /// observed as a rapid-fire reopen loop as the client kept retrying. Confirmed against both
    /// PocketMine (`onClientRemoveWindow` never reads ContainerType at all) and Dragonfly
    /// (`ContainerCloseHandler` switches purely on WindowID) that a real client's ContainerType on
    /// close is not meaningful to validate.
    /// </summary>
    [Fact]
    public void InventorySystem_closes_the_active_chest_even_when_the_client_sends_an_unrelated_window_type()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stale-window-type");
        fx.World.Chests.Ensure(7, 73, 33);

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(7, 73, 33)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.OpenChest);

        Assert.True(player.SubmitWindowIntent(
            InventoryWindowIntent.Close((byte)InventoryContainerMap.WindowChest, 247)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(7, 73, 33));

        // Must still be able to reopen cleanly afterward — the actual observed symptom.
        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(7, 73, 33)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.OpenChest);
        Assert.Equal(1, fx.World.Chests.OpenerCount(7, 73, 33));
    }

    [Fact]
    public void InventorySystem_can_open_the_player_inventory_again_after_closing_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("reopener-inv");

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenInventory()));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.InventoryWindowOpen);

        Assert.True(player.SubmitWindowIntent(
            InventoryWindowIntent.Close((byte)InventoryContainerMap.WindowInventory, ContainerOpenPacket.WindowTypeInventory)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.False(player.InventoryWindowOpen);

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenInventory()));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.InventoryWindowOpen);
    }

    [Fact]
    public void InventorySystem_releases_disconnected_chest_opener_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-opener");
        fx.World.Chests.Ensure(6, 64, 6);
        var inventory = fx.CreateInventorySystem();

        Assert.True(player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(6, 64, 6)));
        inventory.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, fx.World.Chests.OpenerCount(6, 64, 6));

        // The network lifecycle removes the player first, then hands world cleanup to the tick.
        fx.Players.Remove(player);
        fx.Players.SubmitDisconnectedContainerCleanup(player);
        inventory.Tick(fx.Clock, Array.Empty<Player.Player>());

        Assert.Null(player.OpenChest);
        Assert.Equal(0, fx.World.Chests.OpenerCount(6, 64, 6));
    }

    /// <summary>
    /// Regression for ADR §104b's Adendo: inventory/player-data persistence on disconnect used to
    /// run directly from the network thread inside HandleClose, racing InventorySystem's
    /// unsynchronized reads of PlayerInventory on the GameLoop thread. It must instead be queued
    /// and drained on the tick, exactly like the chest-opener release above.
    /// </summary>
    [Fact]
    public void InventorySystem_persists_disconnected_player_inventory_on_the_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("disconnect-persist");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));
        var inventory = fx.CreateInventorySystem();

        // The network lifecycle removes the player first, then hands the persist write to the tick.
        fx.Players.Remove(player);
        fx.Players.SubmitDisconnectedInventoryPersist(player);

        var loaded = new PlayerInventory(seedStarterHotbar: false);
        Assert.False(fx.World.TryLoadInventory(player.Uuid, loaded));

        inventory.Tick(fx.Clock, Array.Empty<Player.Player>());

        Assert.True(fx.World.TryLoadInventory(player.Uuid, loaded));
        Assert.Equal(Blocks.Stone, loaded.Get(0).Id.Value);
        Assert.Equal(5, loaded.Get(0).Count);
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
