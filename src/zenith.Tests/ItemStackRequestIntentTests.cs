using System.Collections.Concurrent;
using System.Net;
using Zenith.Ecs;
using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Logging;
using Zenith.Raknet.Network;
using Zenith.Binary;
using Zenith.Server;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>ItemStackRequest decoding and the creative catalog it draws from.</summary>
public class ItemStackRequestIntentTests
{

    [Fact]
    public void ItemStackRequest_rejects_crafting_when_player_inventory_ui_is_not_open()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("closed-craft");
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 501, TestRecipes.OakLogToPlanks);

        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }


    [Fact]
    public void ItemStackRequest_after_Interact_open_inventory_commits_on_the_same_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-then-creative", GameMode.Creative);

        // The client may send the first creative request before the GameLoop has materialised
        // the ContainerOpen. Both packets are correctly ordered on the inbound stream.
        var interact = new BinaryStream();
        interact.WriteByte(InteractPacket.ActionOpenInventory);
        interact.WriteUnsignedVarLong(player.RuntimeId);
        interact.WriteBool(false); // no position
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.INTERACT_PACKET },
            ref interact));

        var request = new BinaryStream();
        request.WriteUnsignedVarInt(1); // request count
        request.WriteVarInt(613);
        request.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref request, ItemStackRequestPacket.ActionCraftCreative);
        request.WriteUnsignedVarInt(checked((int)CreativeCatalog.Stone));
        request.WriteByte(1); // number of crafts (wire boilerplate)
        request.WriteUnsignedVarInt(0); // filter strings
        request.WriteInt(0, BinaryStream.Endianess.Little); // filter cause
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref request));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.InventoryWindowOpen);
        Assert.Equal(StackId.FromBlock(Blocks.Stone), player.Windows.CraftUi.Result.Id);
        Assert.Equal(PlayerInventory.MaxStack, player.Windows.CraftUi.Result.Count);
    }


    [Fact]
    public void ItemStackRequest_after_pending_chest_open_commits_on_the_same_gameplay_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("open-chest-then-transfer");
        fx.World.SetBlock(2, 64, 2, Blocks.Chest);
        Assert.True(fx.World.Chests.TryEnsure(2, 64, 2));
        StandNear(player, 2, 64, 2);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Dirt, 2));

        // The block interaction publishes its window request; the immediately following ISR
        // must bind to that pending chest rather than be rejected for an absent current view.
        Assert.True(player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(2, 64, 2)));
        SubmitChestTransferRequest(player, requestId: 614);

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(StackId.FromBlock(Blocks.Dirt), fx.World.Chests.Get(2, 64, 2, 0).Id);
        Assert.Equal(1, fx.World.Chests.Get(2, 64, 2, 0).Count);
    }


    [Fact]
    public void ItemStackRequest_rejects_a_dynamic_container_id_for_a_static_hotbar_slot()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dynamic-container");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 2));
        var slotOneBefore = player.Inventory.Get(1);

        var request = new BinaryStream();
        request.WriteUnsignedVarInt(1);
        request.WriteVarInt(615);
        request.WriteUnsignedVarInt(1);
        WriteStackActionHeader(ref request, ItemStackRequestPacket.ActionPlace);
        request.WriteByte(1);
        WriteDynamicSlotInfo(ref request, InventoryContainerMap.Hotbar, 0, dynamicId: 42);
        WriteSlotInfo(ref request, InventoryContainerMap.Hotbar, 1);
        SubmitItemStackRequest(player, request);

        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
        Assert.Equal(2, player.Inventory.Get(0).Count);
        Assert.Equal(slotOneBefore, player.Inventory.Get(1));
    }


    [Fact]
    public void ItemStackRequest_crafting_bound_to_closed_player_inventory_ui_does_not_commit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stale-craft");
        var session = player.Windows.OpenPlayerContainer(0, 0xff);
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 502, TestRecipes.OakLogToPlanks);
        Assert.True(player.Windows.TryCloseContainer(session.WindowId, session.WindowType, out _));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }


    [Fact]
    public void ItemStackRequest_crafting_bound_to_replaced_player_inventory_ui_does_not_commit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("replaced-craft");
        player.Windows.OpenPlayerContainer(0, 0xff);
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 503, TestRecipes.OakLogToPlanks);
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }


    [Fact]
    public void ItemStackRequest_rejects_crafting_while_chest_is_the_active_session()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("chest-craft");
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        SubmitCraftRecipeRequest(player, requestId: 504, TestRecipes.OakLogToPlanks);

        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }


    [Fact]
    public void ItemStackRequest_rejects_cursor_drop_without_an_active_container_session()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("closed-cursor");

        SubmitDropRequest(player, requestId: 505, InventoryContainerMap.Cursor, 0);

        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
    }


    [Fact]
    public void ItemStackRequest_rejects_a_chest_slot_when_player_inventory_ui_is_active()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("wrong-chest");
        player.Windows.OpenPlayerContainer(0, 0xff);

        SubmitChestTransferRequest(player, requestId: 506);

        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
    }


    [Fact]
    public void ItemStackRequest_rejects_a_request_that_spans_chest_and_crafting_ui()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mixed-ui");
        player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));

        SubmitChestAndCraftRequest(player, requestId: 507, TestRecipes.OakLogToPlanks);

        Assert.False(player.Intents.TryConsumeInventoryStack(out _));
    }


    [Fact]
    public void Creative_join_has_empty_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative", GameMode.Creative);
        Assert.Equal(GameMode.Creative, player.GameMode);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.Get(i).IsEmpty);
    }


    [Fact]
    public void CreativeContent_starter_encode_has_items()
    {
        Blocks.EnsureLoaded();
        var palette = ItemPaletteLoader.FromEmbeddedResource();
        var packet = InventoryProtocol.BuildCreativeContent(CreativeCatalog.CreateDefault(), palette);
        // ADR §142: real vanilla catalog, filtered by the Blocks+DigProfiles/Tools safety gate —
        // 33 blocks (13 pinned starter blocks + ores/cobblestone/deepslate/leaves/ground-cover, every
        // other block with a real name and a curated DigProfiles row) + 12 curated tools.
        Assert.Equal(33 + 12, packet.Items.Length);
        Assert.Equal(CreativeCatalog.Stone, packet.Items[0].CreativeItemNetworkId);
        Assert.Equal(19, packet.Groups.Length); // real vanilla groups the 45 eligible items span.
        Assert.True(packet.Encode().Length > 16);
    }


    [Fact]
    public void Item_stack_request_creative_pick_to_hotbar_commits_through_the_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-wire", GameMode.Creative);
        player.Windows.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(73); // request id: protocol 2168 VarInt, not fixed little-endian
        writer.WriteUnsignedVarInt(2); // CraftCreative + Place

        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftCreative);
        writer.WriteUnsignedVarInt(checked((int)CreativeCatalog.Stone));
        writer.WriteByte(1); // NumberOfCrafts (protocol boilerplate)

        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(PlayerInventory.MaxStack);
        WriteSlotInfo(
            ref writer,
            InventoryContainerMap.CreatedOutput,
            InventoryContainerMap.CraftingResultWireSlot);
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);

        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        var slot = player.Inventory.Get(0);
        Assert.Equal(Blocks.Stone, slot.Id.Value);
        Assert.Equal(PlayerInventory.MaxStack, slot.Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }


    [Fact]
    public void Item_stack_request_with_an_unbounded_wire_count_is_rejected_without_mutating_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("isr-bounded");
        var before = player.Inventory.Get(0);
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(ItemStackRequestPacket.MaxRequestsPerPacket + 1);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        Assert.True(new InGameSessionHandler().HandleDataPacket(
            player.Session,
            new DataPacket.HeaderInfo { Id = (int)ProtocolInfo.ITEM_STACK_REQUEST_PACKET },
            ref stream));

        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(before, player.Inventory.Get(0));
    }


    [Fact]
    public void ItemStackRequest_creative_destroy_removes_only_the_declared_source_count()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-destroy", GameMode.Creative);
        player.Windows.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(74);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionDestroy);
        writer.WriteByte(2);
        WriteSlotInfo(ref writer, InventoryContainerMap.Hotbar, 0);

        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        var remaining = player.Inventory.Get(0);
        Assert.Equal(Blocks.Stone, remaining.Id.Value);
        Assert.Equal(3, remaining.Count);
    }


    [Fact]
    public void ItemStackRequest_destroy_rejects_survival_without_mutating_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("survival-destroy");
        player.Windows.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(75, [
            InventoryStackAction.Destroy(
                InventorySlotReference.Player(0), 2,
                new WireSlot(InventoryContainerMap.Hotbar, 0))
        ])));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(5, player.Inventory.Get(0).Count);
    }


    [Fact]
    public void ItemStackRequest_mine_block_acknowledges_the_authoritative_hotbar_stack()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mine-stack-request");
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.Stone, 5));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(76);
        writer.WriteUnsignedVarInt(1); // action count
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionMineBlock);
        writer.WriteVarInt(0); // hotbar slot
        writer.WriteVarInt(0); // predicted durability: ignored until durability exists
        writer.WriteInt(0, BinaryStream.Endianess.Little); // no advertised stack id yet

        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        var slot = player.Inventory.Get(0);
        Assert.Equal(Blocks.Stone, slot.Id.Value);
        Assert.Equal(5, slot.Count);
    }


    [Fact]
    public void ItemStackRequest_rejects_create_for_an_unimplemented_result_slot_without_clearing_output()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("create-result-index");
        player.Windows.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.Windows.CraftUi.TrySetResult(InventorySlot.OfBlock(Blocks.Dirt, 3)));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(77);
        writer.WriteUnsignedVarInt(1);
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCreate);
        writer.WriteByte(1);
        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Dirt, player.Windows.CraftUi.Result.Id.Value);
        Assert.Equal(3, player.Windows.CraftUi.Result.Count);
    }


    [Fact]
    public void ItemStackRequest_rejects_zero_craft_times_without_consuming_the_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("zero-craft-times");
        player.Windows.OpenPlayerContainer(
            (byte)InventoryContainerMap.WindowInventory,
            InventoryContainerMap.WindowTypeInventory);
        Assert.True(player.Windows.CraftUi.TrySetGrid(0, InventorySlot.OfBlock(Blocks.OakLog, 1)));

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(78);
        writer.WriteUnsignedVarInt(1);
        WriteStackActionHeader(ref writer, ItemStackRequestPacket.ActionCraftRecipe);
        writer.WriteUnsignedVarInt(checked((int)TestRecipes.OakLogToPlanks));
        writer.WriteByte(0);
        SubmitItemStackRequest(player, writer);
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.OakLog, player.Windows.CraftUi.GetGrid(0).Id.Value);
        Assert.Equal(1, player.Windows.CraftUi.GetGrid(0).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }


    [Fact]
    public void CreativeContent_empty_icon_writes_the_complete_air_network_item_instance_descriptor()
    {
        // Protocol 2193 requires every descriptor field even for air; its user data is empty.
        var emptyPk = new CreativeContentPacket
        {
            Groups = [new CreativeGroupEntry(1, "", NetworkItemStack.Empty)],
            Items = []
        };
        var bytes = emptyPk.Encode().ToArray();
        Assert.Equal(new byte[] { 0x91, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0 }, bytes);

        var writer = new BinaryStream();
        NetworkItemStack.Empty.WriteItemStack(ref writer);
        var air = writer.GetBufferDisposing().ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0 }, air);
    }


    [Fact]
    public void CreativeContent_non_air_icon_still_uses_the_full_empty_lists_descriptor()
    {
        // The counterpart to the air-item test above: a real (non-zero NetworkId) item stack
        // must still get the full NBT-length + can_place/can_break blob, matching
        // gophertunnel's Writer.Item for the present==true case.
        var writer = new BinaryStream();
        new NetworkItemStack(NetworkId: 5, Count: 1, BlockRuntimeId: 0).WriteItemStack(ref writer);
        var bytes = writer.GetBufferDisposing().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal(5, stream.ReadVarInt()); // network id
        Assert.Equal((ushort)1, stream.ReadUShort(BinaryStream.Endianess.Little)); // count
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // meta
        Assert.Equal(0, stream.ReadVarInt()); // block runtime id
        Assert.Equal(10, stream.ReadUnsignedVarInt()); // extra-data length — full blob, not the air short-circuit
        for (var i = 0; i < 10; i++) Assert.Equal(0, stream.ReadByte());
        Assert.True(stream.IsEndOfFile);
    }


    [Fact]
    public void CreativeContent_group_category_is_a_single_byte()
    {
        // Category is a wire mapper<u8> (one byte), not a fixed int32.
        var pk = new CreativeContentPacket
        {
            Groups = [new CreativeGroupEntry(CreativeContentPacket.CategoryConstruction, "", NetworkItemStack.Empty)],
            Items = []
        };
        var bytes = pk.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.CREATIVE_CONTENT_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(1, stream.ReadUnsignedVarInt()); // groups count
        Assert.Equal((byte)1, stream.ReadByte()); // Construction in retail 1.26.52; exactly one byte
        Assert.Equal("", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt()); // empty icon ID
        Assert.Equal((ushort)0, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // metadata
        Assert.Equal(0, stream.ReadVarInt()); // block runtime ID
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // empty user data
        Assert.Equal(0, stream.ReadUnsignedVarInt()); // items count
        Assert.True(stream.IsEndOfFile);
    }
}
