using Xunit;
using Zenith.Packets;
using Zenith.Raknet.Stream;

namespace Zenith.Tests;

/// <summary>
/// Standalone InventoryTransaction / ItemStackRequest decode (not nested in AuthInput).
/// </summary>
public class InventoryTransactionDtoTests
{
    [Fact]
    public void InventoryTransaction_rejects_an_unbounded_action_count_before_allocation()
    {
        var writer = new BinaryStream();
        writer.WriteVarInt(0);
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeNormal);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(InventoryTransactionPacket.MaxActions + 1);

        var bytes = writer.GetBufferDisposing().ToArray();
        Assert.Throws<InvalidDataException>(() => DecodeInventoryTransaction(bytes));
    }

    [Fact]
    public void InventoryTransaction_rejects_an_unbounded_legacy_slot_array()
    {
        var writer = new BinaryStream();
        writer.WriteVarInt(-2);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(InventoryTransactionPacket.MaxLegacySetItemContainers + 1);

        var bytes = writer.GetBufferDisposing().ToArray();
        Assert.Throws<InvalidDataException>(() => DecodeInventoryTransaction(bytes));
    }

    [Fact]
    public void ItemStackRequest_rejects_an_unbounded_request_count_before_allocation()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(ItemStackRequestPacket.MaxRequestsPerPacket + 1);

        var bytes = writer.GetBufferDisposing().ToArray();
        Assert.Throws<InvalidDataException>(() => DecodeItemStackRequest(bytes));
    }

    [Fact]
    public void InventoryTransaction_decodes_UseItem_ClickBlock_standalone()
    {
        var w = new BinaryStream();
        w.WriteVarInt(0); // legacyRequestId
        w.WriteBool(false); // no legacy slots
        w.WriteBool(true); // type marker
        w.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        w.WriteBool(true); // actions marker
        w.WriteUnsignedVarInt(0); // no inventory actions

        w.WriteVarInt(InventoryTransactionPacket.UseClickBlock);
        w.WriteByte(0); // trigger
        w.WriteVarInt(8);
        w.WriteVarInt(-60);
        w.WriteVarInt(12);
        w.WriteByte(1); // face
        w.WriteVarInt(0); // hotbar
        WriteNetworkItemAir(ref w);
        for (var i = 0; i < 6; i++)
            w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteUnsignedVarInt(99); // block under cursor
        w.WriteByte(0);
        w.WriteByte(0);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new InventoryTransactionPacket();
        packet.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(InventoryTransactionPacket.TypeItemUse, packet.TransactionType);
        Assert.Equal(InventoryTransactionPacket.UseClickBlock, packet.UseActionType);
        Assert.Equal(8, packet.BlockX);
        Assert.Equal(-60, packet.BlockY);
        Assert.Equal(12, packet.BlockZ);
        Assert.Equal(0, packet.HotbarSlot);
        Assert.Equal(99, packet.ClickedBlockRuntimeId);
    }

    [Fact]
    public void InventoryTransaction_decodes_UseItem_Destroy_standalone()
    {
        var w = new BinaryStream();
        w.WriteVarInt(0);
        w.WriteBool(false);
        w.WriteBool(true);
        w.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemUse);
        w.WriteBool(true);
        w.WriteUnsignedVarInt(0);

        w.WriteVarInt(InventoryTransactionPacket.UseDestroyBlock);
        w.WriteByte(0);
        w.WriteVarInt(3);
        w.WriteVarInt(4);
        w.WriteVarInt(5);
        w.WriteByte(1);
        w.WriteVarInt(2);
        WriteNetworkItemAir(ref w);
        for (var i = 0; i < 6; i++)
            w.WriteFloat(0, BinaryStream.Endianess.Little);
        w.WriteUnsignedVarInt(0);
        w.WriteByte(0);
        w.WriteByte(0);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new InventoryTransactionPacket();
        packet.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(InventoryTransactionPacket.UseDestroyBlock, packet.UseActionType);
        Assert.Equal(3, packet.BlockX);
        Assert.Equal(4, packet.BlockY);
        Assert.Equal(5, packet.BlockZ);
        Assert.Equal(2, packet.HotbarSlot);
    }

    [Fact]
    public void InventoryTransaction_decodes_ReleaseItem_hotbar_and_held_stack()
    {
        var w = new BinaryStream();
        w.WriteVarInt(0);
        w.WriteBool(false);
        w.WriteBool(true);
        w.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeItemRelease);
        w.WriteBool(true);
        w.WriteUnsignedVarInt(0);

        w.WriteVarInt(0); // release action
        w.WriteVarInt(5); // hotbar slot
        w.WriteShort(17, BinaryStream.Endianess.Little);
        w.WriteUShort(2, BinaryStream.Endianess.Little);
        w.WriteUnsignedVarInt(3);
        w.WriteBool(true);
        w.WriteVarInt(44);
        w.WriteUnsignedVarInt(99);
        w.WriteUnsignedVarInt(0);
        for (var i = 0; i < 3; i++)
            w.WriteFloat(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new InventoryTransactionPacket();
        packet.Decode(ref stream);

        Assert.Equal(InventoryTransactionPacket.TypeItemRelease, packet.TransactionType);
        Assert.Equal(0, packet.ReleaseActionType);
        Assert.Equal(5, packet.HotbarSlot);
        Assert.Equal(17, packet.HeldItem.NetworkId);
        Assert.Equal((ushort)2, packet.HeldItem.Count);
        Assert.Equal(44, packet.HeldItem.StackNetworkId);
        Assert.Equal(99, packet.HeldItem.BlockRuntimeId);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void InventoryTransaction_decodes_legacy_normal_drop_actions()
    {
        var writer = new BinaryStream();
        writer.WriteVarInt(-2); // legacy request id: even negative IDs carry slot sync metadata
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(0); // legacy slot sync entries
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt((int)InventoryTransactionPacket.TypeNormal);
        writer.WriteBool(true);
        writer.WriteUnsignedVarInt(2);

        WriteInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceContainer,
            windowId: 0,
            slot: 3,
            oldItem: new NetworkItemStack(1, 5, 7),
            newItem: new NetworkItemStack(1, 4, 7));
        WriteInventoryAction(
            ref writer,
            InventoryTransactionPacket.SourceWorld,
            windowId: null,
            slot: 0,
            oldItem: NetworkItemStack.Empty,
            newItem: new NetworkItemStack(1, 1, 7));

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var packet = new InventoryTransactionPacket();
        packet.Decode(ref stream);

        Assert.Equal(-2, packet.LegacyRequestId);
        Assert.True(packet.HasLegacySetItemSlots);
        Assert.Equal(InventoryTransactionPacket.TypeNormal, packet.TransactionType);
        Assert.Equal(2, packet.Actions.Length);
        Assert.Equal(InventoryTransactionPacket.SourceContainer, packet.Actions[0].SourceType);
        Assert.Equal((byte)0, packet.Actions[0].WindowId);
        Assert.Equal(5, packet.Actions[0].OldItem.Count);
        Assert.Equal(4, packet.Actions[0].NewItem.Count);
        Assert.Equal(InventoryTransactionPacket.SourceWorld, packet.Actions[1].SourceType);
        Assert.True(packet.Actions[1].OldItem.IsEmpty);
        Assert.Equal(1, packet.Actions[1].NewItem.Count);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void ItemStackRequest_decodes_Place_standalone()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(7); // typed client request id (zigzag varint)
        writer.WriteUnsignedVarInt(1);
        writer.WriteUnsignedVarInt(ItemStackRequestPacket.ActionPlace); // Cereal variant
        writer.WriteByte(ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(3); // count
        writer.WriteByte(28); // hotbar container
        writer.WriteBool(false);
        writer.WriteByte(0);
        writer.WriteInt(10, BinaryStream.Endianess.Little); // stack_id (li32, ADR §90)
        writer.WriteByte(12); // inventory
        writer.WriteBool(false);
        writer.WriteByte(5);
        writer.WriteInt(11, BinaryStream.Endianess.Little); // stack_id (li32, ADR §90)
        writer.WriteUnsignedVarInt(0);
        writer.WriteInt(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref stream);
        stream.Dispose();

        Assert.Single(packet.Requests);
        Assert.Equal(7, packet.Requests[0].RequestId);
        Assert.True(packet.Requests[0].AllSupported);
        Assert.Equal(ItemStackRequestPacket.ActionPlace, packet.Requests[0].Actions[0].ActionType);
        Assert.Equal(3, packet.Requests[0].Actions[0].Count);
        Assert.Equal(28, packet.Requests[0].Actions[0].Source.Container.ContainerId);
        Assert.Equal(12, packet.Requests[0].Actions[0].Destination.Container.ContainerId);
    }

    [Fact]
    public void ItemStackRequest_decodes_creative_create_then_place_sequence()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1); // request count
        writer.WriteVarInt(44); // request id
        writer.WriteUnsignedVarInt(2); // actions

        WriteActionHeader(ref writer, ItemStackRequestPacket.ActionCraftCreative);
        writer.WriteUnsignedVarInt(20); // creative item network id
        writer.WriteByte(1); // craft times

        WriteActionHeader(ref writer, ItemStackRequestPacket.ActionPlace);
        writer.WriteByte(64); // count
        WriteSlotInfo(ref writer, 60, 50, 0); // created output
        WriteSlotInfo(ref writer, 28, 0, 0); // hotbar

        writer.WriteUnsignedVarInt(0); // filter strings
        writer.WriteInt(0, BinaryStream.Endianess.Little); // filter cause

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref stream);

        var request = Assert.Single(packet.Requests);
        Assert.True(request.AllSupported);
        Assert.Equal(ItemStackRequestPacket.ActionCraftCreative, request.Actions[0].ActionType);
        Assert.Equal(20u, request.Actions[0].CreativeNetId);
        Assert.Equal(ItemStackRequestPacket.ActionPlace, request.Actions[1].ActionType);
        Assert.Equal(64, request.Actions[1].Count);
        Assert.Equal(28, request.Actions[1].Destination.Container.ContainerId);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void ItemStackRequest_retains_create_result_slot_for_domain_validation()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(45);
        writer.WriteUnsignedVarInt(1);
        WriteActionHeader(ref writer, ItemStackRequestPacket.ActionCreate);
        writer.WriteByte(2); // a future/multi-result output index, not Zenith's single slot 0
        writer.WriteUnsignedVarInt(0);
        writer.WriteInt(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref stream);

        var action = Assert.Single(Assert.Single(packet.Requests).Actions);
        Assert.Equal(ItemStackRequestPacket.ActionCreate, action.ActionType);
        Assert.Equal(2, action.ResultSlot);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void ItemStackRequest_decodes_the_2168_cereal_variant_and_legacy_id_pair_for_mine_block()
    {
        // The Cereal union omits the obsolete Place/TakeInContainer variants (7/8), while
        // the following legacy id retains them. Thus MineBlock is variant 9 but legacy id 11.
        // Keep the literals here: this is a characterization test against Gophertunnel 2168,
        // not an assertion coupled to Zenith's constants/helper.
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(91);
        writer.WriteUnsignedVarInt(1);
        writer.WriteUnsignedVarInt(9); // cereal variant
        writer.WriteByte(11); // legacy StackRequestActionMineBlock
        writer.WriteVarInt(4); // hotbar slot
        writer.WriteVarInt(0); // predicted durability
        writer.WriteInt(73, BinaryStream.Endianess.Little); // stack network id
        writer.WriteUnsignedVarInt(0); // filter strings
        writer.WriteInt(0, BinaryStream.Endianess.Little); // filter cause

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref stream);

        var action = Assert.Single(Assert.Single(packet.Requests).Actions);
        Assert.True(action.Supported);
        Assert.Equal(ItemStackRequestPacket.ActionMineBlock, action.ActionType);
        Assert.Equal(4, action.HotbarSlot);
        Assert.Equal(73, action.StackNetworkId);
        Assert.True(stream.IsEndOfFile);
    }

    private static void WriteActionHeader(ref BinaryStream writer, byte legacyActionId)
    {
        writer.WriteUnsignedVarInt(legacyActionId < ItemStackRequestPacket.ActionPlaceInContainer
            ? legacyActionId
            : legacyActionId - 2);
        writer.WriteByte(legacyActionId);
    }

    private static void DecodeInventoryTransaction(byte[] bytes)
    {
        var stream = new BinaryStream(bytes);
        new InventoryTransactionPacket().Decode(ref stream);
    }

    private static void DecodeItemStackRequest(byte[] bytes)
    {
        var stream = new BinaryStream(bytes);
        new ItemStackRequestPacket().Decode(ref stream);
    }

    private static void WriteSlotInfo(ref BinaryStream writer, byte containerId, byte slot, int stackNetworkId)
    {
        writer.WriteByte(containerId);
        writer.WriteBool(false);
        writer.WriteByte(slot);
        writer.WriteInt(stackNetworkId, BinaryStream.Endianess.Little);
    }

    private static void WriteNetworkItemAir(ref BinaryStream w)
    {
        w.WriteShort(0, BinaryStream.Endianess.Little);
        w.WriteUShort(0, BinaryStream.Endianess.Little);
        w.WriteUnsignedVarInt(0);
        w.WriteBool(false);
        w.WriteUnsignedVarInt(0);
        w.WriteUnsignedVarInt(0);
    }

    private static void WriteInventoryAction(
        ref BinaryStream writer,
        uint sourceType,
        byte? windowId,
        int slot,
        NetworkItemStack oldItem,
        NetworkItemStack newItem)
    {
        writer.WriteUnsignedVarInt(checked((int)sourceType));
        writer.WriteBool(true); // required WindowID marker
        writer.WriteBool(windowId.HasValue);
        if (windowId is { } window)
            writer.WriteByte(window);
        writer.WriteBool(true); // required SourceFlags marker
        writer.WriteBool(false);
        writer.WriteUnsignedVarInt(slot);
        oldItem.WriteNetworkItemStackDescriptor(ref writer);
        newItem.WriteNetworkItemStackDescriptor(ref writer);
    }
}
