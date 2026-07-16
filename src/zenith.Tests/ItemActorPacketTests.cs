using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class ItemActorPacketTests
{
    [Fact]
    public void AddItemActor_encode_shape_uses_item_stack_wrapper()
    {
        var item = new NetworkItemStack(1, 3, 100);
        var bytes = new AddItemActorPacket
        {
            EntityUniqueId = 42,
            EntityRuntimeId = 42,
            Item = item,
            PositionX = 1.5f,
            PositionY = 64.125f,
            PositionZ = -2.5f,
            FromFishing = false
        }.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.ADD_ITEM_ACTOR_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(42L, stream.ReadVarLong());
        Assert.Equal(42ul, (ulong)stream.ReadUnsignedVarLong());
        // ItemStackWrapper / legacy ItemInstance (same as AddPlayer held) — not ItemInstanceNew i16.
        Assert.Equal(1, stream.ReadVarInt());
        Assert.Equal((ushort)3, stream.ReadUShort(BinaryStream.Endianess.Little));
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // meta
        Assert.False(stream.ReadBool()); // no stack net id
        Assert.Equal(100, stream.ReadVarInt()); // block runtime
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // extra
        Assert.Equal(1.5f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(64.125f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(-2.5f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(0f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(0f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(0f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // metadata count
        Assert.False(stream.ReadBool());
    }

    [Fact]
    public void AddItemActor_air_item_is_single_varint_zero()
    {
        var bytes = new AddItemActorPacket
        {
            EntityUniqueId = 1,
            EntityRuntimeId = 1,
            Item = NetworkItemStack.Empty,
            PositionX = 0f,
            PositionY = 0f,
            PositionZ = 0f
        }.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        _ = stream.ReadUnsignedVarInt();
        _ = stream.ReadVarLong();
        _ = stream.ReadUnsignedVarLong();
        Assert.Equal(0, stream.ReadVarInt());
    }

    [Fact]
    public void TakeItemActor_encode_shape()
    {
        var bytes = new TakeItemActorPacket
        {
            ItemEntityRuntimeId = 9,
            TakerEntityRuntimeId = 1
        }.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.TAKE_ITEM_ACTOR_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(9ul, (ulong)stream.ReadUnsignedVarLong());
        Assert.Equal(1ul, (ulong)stream.ReadUnsignedVarLong());
    }
}
