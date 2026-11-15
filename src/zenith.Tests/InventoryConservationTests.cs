using Zenith.Binary;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Session.Handler;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class InventoryConservationTests
{
    [Fact]
    public void Death_drops_each_staged_output_without_counting_the_visible_one_twice()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("result-death");
        player.Inventory.Clear();
        var sugar = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:sugar"));
        var bottle = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:glass_bottle"));
        player.Windows.CraftUi.TryStageOutputs([new InventorySlot(sugar, 3), new InventorySlot(bottle, 1)]);
        player.Windows.CraftUi.TrySelectOutput(1);
        Assert.True(FloorDropFanout.TryDropDeathLoot(fx.World, fx.Players, fx.Players.Online, player));
        Assert.Equal(3, Assert.Single(fx.World.FloorDrops.Snapshot(), drop => drop.Id == sugar).Count);
        Assert.Equal(1, Assert.Single(fx.World.FloorDrops.Snapshot(), drop => drop.Id == bottle).Count);
        Assert.False(player.Windows.CraftUi.HasPendingOutputs);
    }

    [Fact]
    public void Bad_inventory_envelope_does_not_partially_replace_the_bag()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("bad-envelope");
        var data = PlayerInventorySave.Pack(player);
        var target = NewPrivatePlayer(fx, player); target.Inventory.Clear();
        target.Inventory.TrySetBlock(0, Blocks.Dirt, 3);
        Assert.False(PlayerInventorySave.TryLoad(data.AsSpan(0, data.Length - 1), target.Inventory, target.Windows));
        Assert.Equal(InventorySlot.OfBlock(Blocks.Dirt, 3), target.Inventory.Get(0));
    }

    [Fact]
    public void Untaken_secondary_output_prevents_a_second_craft_from_overwriting_it()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("result-busy");
        player.Inventory.Clear(); player.Windows.OpenPlayerContainer(0, 255);
        var honey = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:honey_bottle"));
        player.Inventory.TrySet(0, honey, 2);
        var recipe = fx.Context.Recipes.NetIdOf("minecraft:honey_bottle_to_sugar");
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(901, [
            InventoryStackAction.CraftAuto(recipe),
            InventoryStackAction.Transfer(InventorySlotReference.CraftResult, InventorySlotReference.Player(1), 3)
        ]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(902, [InventoryStackAction.CraftAuto(recipe)]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(3, player.Inventory.Get(1).Count);
        Assert.True(player.Windows.CraftUi.TrySelectOutput(1));
        Assert.Equal(1, player.Windows.CraftUi.Result.Count);
    }

    [Fact]
    public void Single_result_api_refuses_secondary_outputs_before_consuming()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("single-result-refusal");
        var honey = InventorySlot.OfItem(fx.Context.ItemPalette.Require("minecraft:honey_bottle"), 1);
        player.Windows.CraftUi.TrySetGrid(0, honey);
        Assert.False(fx.Context.Recipes.TryCraftFromGrid(player.Windows.CraftUi,
            fx.Context.Recipes.NetIdOf("minecraft:honey_bottle_to_sugar"), out _, 1, false));
        Assert.Equal(honey, player.Windows.CraftUi.GetGrid(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Honey_craft_delivers_sugar_and_bottle_once(bool automatic)
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("multi-result");
        player.Inventory.Clear(); player.Windows.OpenPlayerContainer(0, 255);
        var honey = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:honey_bottle"));
        if (automatic) player.Inventory.TrySet(0, honey, 1);
        else player.Windows.CraftUi.TrySetGrid(0, new InventorySlot(honey, 1));
        var recipe = fx.Context.Recipes.NetIdOf("minecraft:honey_bottle_to_sugar");
        var intent = InventoryStackIntent.Create(801, [
            automatic ? InventoryStackAction.CraftAuto(recipe, 1) : InventoryStackAction.Craft(recipe),
            InventoryStackAction.CreateOutput(1),
            InventoryStackAction.Transfer(InventorySlotReference.CraftResult, InventorySlotReference.Player(0), 1),
            InventoryStackAction.CreateOutput(0),
            InventoryStackAction.Transfer(InventorySlotReference.CraftResult, InventorySlotReference.Player(1), 3)
        ]);
        Assert.True(player.Intents.SubmitInventoryStack(intent));
        Assert.True(player.Intents.SubmitInventoryStack(intent)); // Replay cannot produce the containers again.
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:glass_bottle")), player.Inventory.Get(0).Id);
        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:sugar")), player.Inventory.Get(1).Id);
        Assert.Equal(3, player.Inventory.Get(1).Count);
        Assert.False(player.Windows.CraftUi.HasPendingOutputs);
    }

    [Fact]
    public void Wire_create_selects_secondary_then_primary_output()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("multi-result-wire");
        player.Inventory.Clear(); player.Windows.OpenPlayerContainer(0, 255);
        player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfItem(fx.Context.ItemPalette.Require("minecraft:honey_bottle"), 1));
        var w = new BinaryStream(); w.WriteUnsignedVarInt(1); w.WriteVarInt(-101); w.WriteUnsignedVarInt(5);
        w.WriteUnsignedVarInt(10); w.WriteByte(12);
        w.WriteUnsignedVarInt((int)fx.Context.Recipes.NetIdOf("minecraft:honey_bottle_to_sugar")); w.WriteByte(1);
        foreach (var index in new byte[] { 1, 0 })
        {
            w.WriteUnsignedVarInt(6); w.WriteByte(6); w.WriteByte(index);
            w.WriteUnsignedVarInt(1); w.WriteByte(1); w.WriteByte(index == 1 ? (byte)1 : (byte)3);
            Slot(ref w, 60, 50); Slot(ref w, 28, index == 1 ? (byte)0 : (byte)1);
        }
        w.WriteUnsignedVarInt(0); w.WriteInt(0, BinaryStream.Endianess.Little);
        var reader = new BinaryStream(w.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET }, ref reader));
        reader.Dispose(); fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, player.Inventory.Get(0).Count); Assert.Equal(3, player.Inventory.Get(1).Count);
        Assert.False(player.Windows.CraftUi.HasPendingOutputs);
    }

    [Fact]
    public void Invalid_output_index_rolls_back_ingredients_and_all_pending_results()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("bad-result");
        player.Inventory.Clear(); player.Windows.OpenPlayerContainer(0, 255);
        var honey = InventorySlot.OfItem(fx.Context.ItemPalette.Require("minecraft:honey_bottle"), 1);
        player.Windows.CraftUi.TrySetGrid(0, honey);
        player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(802, [
            InventoryStackAction.Craft(fx.Context.Recipes.NetIdOf("minecraft:honey_bottle_to_sugar")),
            InventoryStackAction.CreateOutput(200)
        ]));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(honey, player.Windows.CraftUi.GetGrid(0));
        Assert.False(player.Windows.CraftUi.HasPendingOutputs);
        Assert.All(player.Inventory.SnapshotMainInventory(), slot => Assert.True(slot.IsEmpty));
    }

    [Fact]
    public async Task Disconnect_save_recovers_cursor_grids_and_unselected_results()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("disconnect-custody");
        player.Inventory.Clear();
        player.Inventory.TrySet(PlayerInventory.CursorSlot, StackId.FromBlock(Blocks.Stone), 2);
        player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakPlanks, 3));
        player.Windows.TableCraftUi.TrySetGrid(8, InventorySlot.OfBlock(Blocks.Dirt, 4));
        player.Windows.CraftUi.TryStageOutputs([InventorySlot.OfItem(fx.Context.ItemPalette.Require("minecraft:sugar"), 3),
            InventorySlot.OfItem(fx.Context.ItemPalette.Require("minecraft:glass_bottle"), 1)]);
        player.Windows.CraftUi.TrySelectOutput(1); // Do not save only the shared visible slot.
        player.IsInGame = false;
        fx.Players.SubmitDisconnectedInventoryPersist(player);
        fx.CreateInventorySystem().Tick(fx.Clock, []);
        await fx.World.FlushPersistenceAsync();
        var restored = NewPrivatePlayer(fx, player);
        Assert.True(fx.World.TryLoadInventory(restored));
        Assert.Equal(13, restored.Inventory.SnapshotMainInventory().Sum(slot => slot.Count));
        Assert.True(restored.Inventory.Cursor.IsEmpty);
        Assert.False(restored.Windows.CraftUi.HasPendingOutputs);
        Assert.True(restored.Windows.TableCraftUi.GetGrid(8).IsEmpty);
        Assert.False(restored.Windows.RecoverUiToInventory(restored.Inventory));
    }

    [Fact]
    public void Full_bag_keeps_overflow_across_reloads_and_recovers_only_when_space_is_available()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("full-recovery");
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++) player.Inventory.TrySetBlock(i, Blocks.Stone, 64);
        var diamonds = InventorySlot.OfItem(fx.Context.ItemPalette.Require("minecraft:diamond"), 3);
        player.Inventory.TrySet(PlayerInventory.CursorSlot, diamonds.Id, diamonds.Count);
        var restored = NewPrivatePlayer(fx, player);
        Assert.True(PlayerInventorySave.TryLoad(PlayerInventorySave.Pack(player), restored.Inventory, restored.Windows));
        Assert.Equal(diamonds, restored.Inventory.Cursor);
        Assert.False(restored.Windows.RecoverUiToInventory(restored.Inventory));
        var again = NewPrivatePlayer(fx, player);
        Assert.True(PlayerInventorySave.TryLoad(PlayerInventorySave.Pack(restored), again.Inventory, again.Windows));
        Assert.Equal(diamonds, again.Inventory.Cursor);
        again.Inventory.TrySetBlock(0, Blocks.Air, 0);
        Assert.True(again.Windows.RecoverUiToInventory(again.Inventory));
        Assert.Equal(diamonds, again.Inventory.Get(0)); Assert.True(again.Inventory.Cursor.IsEmpty);
        Assert.False(again.Windows.RecoverUiToInventory(again.Inventory));
    }

    [Fact]
    public void Legacy_bag_blob_remains_readable()
    {
        var fx = new IntentTestFixture(); var player = fx.AddInGamePlayer("legacy-save");
        var restored = NewPrivatePlayer(fx, player);
        Assert.True(PlayerInventorySave.TryLoad(player.Inventory.PackMainBlob(), restored.Inventory, restored.Windows));
        Assert.Equal(player.Inventory.SnapshotMainInventory(), restored.Inventory.SnapshotMainInventory());
    }

    private static Player.Player NewPrivatePlayer(IntentTestFixture fx, Player.Player original) =>
        new("restored", original.Session, fx.Players.AllocateRuntimeId(), original.Uuid,
            level: fx.World, itemPalette: fx.Context.ItemPalette);

    private static void Slot(ref BinaryStream writer, byte container, byte index)
    {
        writer.WriteByte(container); writer.WriteBool(false); writer.WriteByte(index);
        writer.WriteInt(0, BinaryStream.Endianess.Little);
    }
}
