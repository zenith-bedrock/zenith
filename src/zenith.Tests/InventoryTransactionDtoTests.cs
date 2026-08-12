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
        w.WriteUnsignedVarInt(0); // block under cursor
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
    public void ItemStackRequest_decodes_Place_standalone()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteInt(7, BinaryStream.Endianess.Little); // Cereal request id (li32)
        writer.WriteUnsignedVarInt(1);
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

    private static void WriteNetworkItemAir(ref BinaryStream w)
    {
        w.WriteShort(0, BinaryStream.Endianess.Little);
        w.WriteUShort(0, BinaryStream.Endianess.Little);
        w.WriteUnsignedVarInt(0);
        w.WriteBool(false);
        w.WriteUnsignedVarInt(0);
        w.WriteUnsignedVarInt(0);
    }
}
