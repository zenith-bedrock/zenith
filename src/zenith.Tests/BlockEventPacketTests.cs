using Xunit;
using Zenith.Packets;
using Zenith.Raknet.Stream;

namespace Zenith.Tests;

public class BlockEventPacketTests
{
    [Fact]
    public void Encode_ChangeChestState_open_shape()
    {
        var packet = new BlockEventPacket
        {
            X = 10,
            Y = -60,
            Z = -3,
            EventType = BlockEventPacket.EventChangeChestState,
            EventData = BlockEventPacket.ChestStateOpen
        };

        var encoded = packet.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.BLOCK_EVENT_PACKET, stream.ReadUnsignedVarInt());

        var decoded = new BlockEventPacket();
        decoded.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(10, decoded.X);
        Assert.Equal(-60, decoded.Y);
        Assert.Equal(-3, decoded.Z);
        Assert.Equal(BlockEventPacket.EventChangeChestState, decoded.EventType);
        Assert.Equal(BlockEventPacket.ChestStateOpen, decoded.EventData);
    }

    [Fact]
    public void Encode_ChangeChestState_close_data_is_zero()
    {
        var packet = new BlockEventPacket
        {
            X = 0,
            Y = 64,
            Z = 0,
            EventType = BlockEventPacket.EventChangeChestState,
            EventData = BlockEventPacket.ChestStateClosed
        };

        var encoded = packet.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        _ = stream.ReadUnsignedVarInt();
        var decoded = new BlockEventPacket();
        decoded.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(0, decoded.EventData);
    }
}
