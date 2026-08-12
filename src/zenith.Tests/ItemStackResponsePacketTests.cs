using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Byte-level Cereal wire checks: both response levels emit their required marker, then their
/// actual optional-presence bit.
/// </summary>
public class ItemStackResponsePacketTests
{
    [Fact]
    public void Error_writes_status_li32_then_required_true_and_no_containers()
    {
        var bytes = ItemStackResponsePacket.Error(5).Encode().ToArray();
        var stream = new BinaryStream(bytes);

        Assert.Equal((int)ProtocolInfo.ITEM_STACK_RESPONSE_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(1, (int)stream.ReadUnsignedVarInt()); // responses count
        Assert.Equal(ItemStackResponseEntry.StatusError, stream.ReadByte());
        Assert.Equal(5, stream.ReadInt(BinaryStream.Endianess.Little));
        Assert.True(stream.ReadBool()); // required marker
        Assert.False(stream.ReadBool()); // containers option
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void Ok_with_stack_id_writes_required_markers_and_zigzag_payload()
    {
        var bytes = ItemStackResponsePacket.Ok(9, [
            new StackResponseContainerInfo
            {
                Container = new FullContainerName { ContainerId = 12 },
                SlotInfo =
                [
                    new StackResponseSlotInfo { Slot = 3, HotbarSlot = 3, Count = 2, StackNetworkId = 7 }
                ]
            }
        ]).Encode().ToArray();
        var stream = new BinaryStream(bytes);

        Assert.Equal((int)ProtocolInfo.ITEM_STACK_RESPONSE_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(1, (int)stream.ReadUnsignedVarInt()); // responses count
        Assert.Equal(ItemStackResponseEntry.StatusOk, stream.ReadByte());
        Assert.Equal(9, stream.ReadInt(BinaryStream.Endianess.Little));
        Assert.True(stream.ReadBool()); // required marker
        Assert.True(stream.ReadBool()); // containers option
        Assert.Equal(1, (int)stream.ReadUnsignedVarInt()); // containers array count

        // FullContainerName
        Assert.Equal(12, stream.ReadByte());
        Assert.False(stream.ReadBool()); // dynamic id option

        Assert.Equal(1, (int)stream.ReadUnsignedVarInt()); // slots count
        Assert.Equal(3, stream.ReadByte()); // slot
        Assert.Equal(3, stream.ReadByte()); // hotbar_slot
        Assert.Equal(2, stream.ReadByte()); // count
        Assert.True(stream.ReadBool()); // required marker
        Assert.True(stream.ReadBool()); // item_stack_id option
        Assert.Equal(7, stream.ReadVarInt()); // item_stack_id payload
        Assert.Equal("", stream.ReadVarString());
        Assert.Equal("", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt());
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void Ok_without_stack_id_writes_required_true_then_optional_false()
    {
        var bytes = ItemStackResponsePacket.Ok(1, [
            new StackResponseContainerInfo
            {
                Container = new FullContainerName { ContainerId = 12 },
                SlotInfo = [new StackResponseSlotInfo { Slot = 0, HotbarSlot = 0, Count = 1, StackNetworkId = 0 }]
            }
        ]).Encode().ToArray();
        var stream = new BinaryStream(bytes);

        stream.ReadUnsignedVarInt(); // id
        stream.ReadUnsignedVarInt(); // responses count
        stream.ReadByte(); // status
        stream.ReadInt(BinaryStream.Endianess.Little); // request id (li32)
        Assert.True(stream.ReadBool()); // required marker
        stream.ReadBool(); // containers option
        stream.ReadUnsignedVarInt(); // containers count
        stream.ReadByte(); // container id
        stream.ReadBool(); // dynamic id option
        stream.ReadUnsignedVarInt(); // slots count
        stream.ReadByte(); // slot
        stream.ReadByte(); // hotbar_slot
        stream.ReadByte(); // count

        Assert.True(stream.ReadBool()); // required marker
        Assert.False(stream.ReadBool()); // item_stack_id option
        Assert.Equal("", stream.ReadVarString());
        Assert.Equal("", stream.ReadVarString());
        Assert.Equal(0, stream.ReadVarInt());
        Assert.True(stream.IsEndOfFile);
    }
}
