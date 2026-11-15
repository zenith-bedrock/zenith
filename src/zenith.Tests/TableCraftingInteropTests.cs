using Zenith.Binary;
using Zenith.Gameplay.Inventory;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Session.Handler;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class TableCraftingInteropTests
{
    [Fact]
    public void Personal_grid_can_bootstrap_a_crafting_table_from_the_registered_recipe()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("bootstrap-table");
        player.Inventory.Clear(); player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 4);
        Open(fx, player, false);
        for (byte i = 0; i < 4; i++) Send(player, Transfer(-1 - i * 2, 28, 0, 13, (byte)(28 + i)));
        var inventory = fx.CreateInventorySystem(); inventory.Tick(fx.Clock, fx.Players.Online);
        var w = new BinaryStream(); w.WriteUnsignedVarInt(1); w.WriteVarInt(-21); w.WriteUnsignedVarInt(3);
        w.WriteUnsignedVarInt(10); w.WriteByte(12); w.WriteUnsignedVarInt((int)TestRecipes.CraftingTable); w.WriteByte(1);
        w.WriteUnsignedVarInt(6); w.WriteByte(6); w.WriteByte(0);
        w.WriteUnsignedVarInt(1); w.WriteByte(1); w.WriteByte(1); Slot(ref w, 60, 50); Slot(ref w, 28, 1);
        w.WriteUnsignedVarInt(0); w.WriteInt(0, BinaryStream.Endianess.Little);
        Send(player, w.GetBufferDisposing().ToArray()); inventory.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(InventorySlot.OfBlock(Blocks.CraftingTable, 1), player.Inventory.Get(1));
        Assert.True(player.Inventory.Get(0).IsEmpty);
        for (var i = 0; i < 4; i++) Assert.True(player.Windows.CraftUi.GetGrid(i).IsEmpty);
    }

    [Fact]
    public void Admission_keeps_reserved_generation_while_open_is_being_applied()
    {
        var windows = new PlayerWindows(() => false);
        Assert.True(windows.SubmitWindowIntent(InventoryWindowIntent.OpenInventory()));
        Assert.True(windows.TryConsumeInventoryRequest(out var window, out var stack));
        Assert.Null(stack);
        Assert.True(windows.TryGetInventoryAdmissionSession(out var during));
        Assert.Equal(window!.Value.Generation, during.Generation);
        var actual = windows.OpenPlayerContainer(0, 255, window.Value.Generation);
        windows.CompleteInventoryWindow();
        Assert.True(windows.TryGetInventoryAdmissionSession(out var after));
        Assert.Equal(actual, after);
    }

    [Fact]
    public void Refused_new_chest_open_cannot_redirect_transfer_to_previous_chest()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("refused-open");
        player.Inventory.Clear(); player.PositionX = 1; player.PositionY = 64; player.PositionZ = 1;
        fx.World.SetBlock(1, 64, 1, Blocks.Chest); fx.World.SetBlock(3, 64, 1, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(1, 64, 1));
        Assert.True(fx.World.Chests.TrySet(1, 64, 1, 0, new InventorySlot(StackId.FromBlock(Blocks.Dirt), 1)));
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        for (var i = 0; i < ChestStore.SoftCap - 1; i++)
            Assert.True(fx.World.Chests.TryEnsure(100 + i, 64, 100));
        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(3, 64, 1)));
        Send(player, Transfer(-1, 7, 0, 28, 0));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.Equal(1, fx.World.Chests.Get(1, 64, 1, 0).Count);

        // A failed intermediate open must also allow a following open of the still-visible UI
        // to establish its reserved generation, without sending a duplicate ContainerOpen.
        Open(fx, player, false);
        player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 1);
        Send(player, Transfer(-3, 28, 0, 13, 28));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Inventory.Get(0).IsEmpty);
        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(3, 64, 1)));
        Open(fx, player, false);
        Send(player, Transfer(-5, 13, 28, 28, 0));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.True(player.Windows.CraftUi.GetGrid(0).IsEmpty);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Cleanup_before_close_commits_once_and_reopen_keeps_items(bool table, bool cursor)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("close-cleanup");
        player.Inventory.Clear(); player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 1);
        Open(fx, player, table);
        var sourceContainer = (byte)(cursor ? 59 : 13);
        var sourceSlot = (byte)(cursor ? 0 : table ? 32 : 28);
        Send(player, Transfer(-1, 28, 0, sourceContainer, sourceSlot));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Inventory.Get(0).IsEmpty);

        Send(player, Transfer(-3, sourceContainer, sourceSlot, 28, 0));
        Close(player);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.Windows.OpenContainer);
        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.True(player.Windows.CraftUi.GetGrid(0).IsEmpty);
        Assert.True(player.Windows.TableCraftUi.GetGrid(0).IsEmpty);

        Open(fx, player, table);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Send(player, Transfer(-3, sourceContainer, sourceSlot, 28, 0)); // Replay cannot repeat the return.
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Empty(fx.World.FloorDrops.Snapshot());
    }

    [Fact]
    public void Pending_open_transfer_close_reopen_and_transfer_preserve_wire_order()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("same-tick-lifecycle");
        player.Inventory.Clear(); player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 2);
        Open(fx, player, true);
        Send(player, Transfer(-1, 28, 0, 13, 32));
        Send(player, Transfer(-3, 13, 32, 28, 0));
        Send(player, [(byte)3, (byte)1, (byte)0], ProtocolInfo.CONTAINER_CLOSE_PACKET);
        Send(player, Transfer(-5, 28, 0, 13, 32)); // A request after close has no UI session.
        Open(fx, player, true);
        Send(player, Transfer(-7, 28, 0, 13, 33));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Windows.IsAtCraftingTable);
        Assert.True(player.Windows.TableCraftUi.GetGrid(0).IsEmpty);
        Assert.Equal(1, player.Windows.TableCraftUi.GetGrid(1).Count);
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Overflow_cleanup_drop_precedes_close_and_is_not_duplicated()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("close-overflow");
        player.Inventory.Clear(); player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 1);
        Open(fx, player, true); Send(player, Transfer(-1, 28, 0, 13, 32));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            player.Inventory.TrySetBlock(i, Blocks.Stone, PlayerInventory.MaxStack);
        var w = new BinaryStream(); w.WriteUnsignedVarInt(1); w.WriteVarInt(-3); w.WriteUnsignedVarInt(1);
        w.WriteUnsignedVarInt(3); w.WriteByte(3); w.WriteByte(1); Slot(ref w, 13, 32); w.WriteBool(false);
        w.WriteUnsignedVarInt(0); w.WriteInt(0, BinaryStream.Endianess.Little);
        Send(player, w.GetBufferDisposing().ToArray()); Close(player);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Null(player.Windows.OpenContainer);
        Assert.True(player.Windows.TableCraftUi.GetGrid(0).IsEmpty);
        var drop = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(Blocks.OakPlanks, drop.Id.Value); Assert.Equal(1, drop.Count);
    }

    private static void Open(IntentTestFixture fx, Player.Player player, bool table)
    {
        if (table)
        {
            fx.World.SetBlock(1, (int)player.PositionY, 0, Blocks.CraftingTable);
            Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenCraftingTable(1, (int)player.PositionY, 0)));
        }
        else Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenInventory()));
    }

    private static void Close(Player.Player player)
    {
        var session = player.Windows.OpenContainer!.Value;
        Send(player, [session.WindowId, session.WindowType, 0], ProtocolInfo.CONTAINER_CLOSE_PACKET);
    }

    [Fact]
    public void Wire_transfers_fill_table_and_recipe_request_crafts_in_that_session()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("table-wire");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 8));
        Assert.True(player.Inventory.TrySetBlock(1, Blocks.Air, 0));
        fx.World.SetBlock(1, (int)player.PositionY, 0, Blocks.CraftingTable);
        player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenCraftingTable(1, (int)player.PositionY, 0));
        var cells = new[] { 0, 1, 2, 3, 5, 6, 7, 8 };
        for (var i = 0; i < cells.Length; i++)
            Send(player, Transfer(-1 - i * 2, 28, 0, 13, (byte)(32 + cells[i])));
        var inventory = fx.CreateInventorySystem(); inventory.Tick(fx.Clock, fx.Players.Online);
        Assert.True(player.Windows.IsAtCraftingTable);
        foreach (var cell in cells) Assert.Equal(1, player.Windows.TableCraftUi.GetGrid(cell).Count);
        Assert.True(player.Inventory.Get(0).IsEmpty);

        var w = new BinaryStream(); w.WriteUnsignedVarInt(1); w.WriteVarInt(-21); w.WriteUnsignedVarInt(3);
        w.WriteUnsignedVarInt(10); w.WriteByte(12); // CraftRecipe union index / legacy enum
        w.WriteUnsignedVarInt((int)TestRecipes.Chest); w.WriteByte(1);
        w.WriteUnsignedVarInt(6); w.WriteByte(6); w.WriteByte(0); // Create output
        w.WriteUnsignedVarInt(1); w.WriteByte(1); w.WriteByte(1);
        Slot(ref w, 60, 50); Slot(ref w, 28, 1);
        w.WriteUnsignedVarInt(0); w.WriteInt(0, BinaryStream.Endianess.Little);
        Send(player, w.GetBufferDisposing().ToArray()); inventory.Tick(fx.Clock, fx.Players.Online);
        Assert.True(Blocks.IsChest(player.Inventory.Get(1).Id.Value));
        Assert.Equal(1, player.Inventory.Get(1).Count);
        foreach (var cell in cells) Assert.True(player.Windows.TableCraftUi.GetGrid(cell).IsEmpty);
    }

    [Fact]
    public void Table_transfer_bound_to_old_generation_cannot_mutate_reopened_table()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("stale-table-wire");
        player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 2);
        player.Windows.OpenCraftingTableContainer(3, 1);
        Send(player, Transfer(-1, 28, 0, 13, 32));
        player.Windows.TryClearOpenContainer(out _);
        player.Windows.OpenCraftingTableContainer(3, 1);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.True(player.Windows.TableCraftUi.GetGrid(0).IsEmpty);
    }

    private static byte[] Transfer(int request, byte source, byte sourceSlot, byte destination, byte destinationSlot)
    {
        var w = new BinaryStream(); w.WriteUnsignedVarInt(1); w.WriteVarInt(request); w.WriteUnsignedVarInt(1);
        w.WriteUnsignedVarInt(1); w.WriteByte(1); w.WriteByte(1);
        Slot(ref w, source, sourceSlot); Slot(ref w, destination, destinationSlot);
        w.WriteUnsignedVarInt(0); w.WriteInt(0, BinaryStream.Endianess.Little);
        return w.GetBufferDisposing().ToArray();
    }

    private static void Slot(ref BinaryStream w, byte container, byte slot)
    {
        w.WriteByte(container); w.WriteBool(false); w.WriteByte(slot); w.WriteInt(0, BinaryStream.Endianess.Little);
    }

    private static void Send(Player.Player player, byte[] body, ProtocolInfo packet = ProtocolInfo.ITEM_STACK_REQUEST_PACKET)
    {
        var r = new BinaryStream(body);
        try { new InGameSessionHandler().HandleDataPacket(player.Session,
            new DataPacket.HeaderInfo { Id = (int)packet }, ref r); }
        finally { r.Dispose(); }
    }
}
