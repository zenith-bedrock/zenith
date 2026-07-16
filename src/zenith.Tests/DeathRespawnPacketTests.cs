using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class DeathRespawnPacketTests
{
    [Fact]
    public void Respawn_encode_decode_shape()
    {
        var bytes = new RespawnPacket
        {
            PositionX = 1.5f,
            PositionY = -58.38f,
            PositionZ = -2.5f,
            State = RespawnPacket.StateReadyToSpawn,
            EntityRuntimeId = 7
        }.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.RESPAWN_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(1.5f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(-58.38f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(-2.5f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(RespawnPacket.StateReadyToSpawn, stream.ReadByte());
        Assert.Equal(7ul, (ulong)stream.ReadUnsignedVarLong());

        var again = new BinaryStream(bytes);
        _ = again.ReadUnsignedVarInt();
        var decoded = new RespawnPacket();
        decoded.Decode(ref again);
        Assert.Equal(1.5f, decoded.PositionX);
        Assert.Equal(-58.38f, decoded.PositionY);
        Assert.Equal(-2.5f, decoded.PositionZ);
        Assert.Equal(RespawnPacket.StateReadyToSpawn, decoded.State);
        Assert.Equal(7ul, decoded.EntityRuntimeId);
    }

    [Fact]
    public void DeathInfo_encode_shape()
    {
        var bytes = new DeathInfoPacket
        {
            Cause = "generic",
            Messages = ["Alice"]
        }.Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.DEATH_INFO_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal("generic", stream.ReadVarString());
        Assert.Equal(1, (int)stream.ReadUnsignedVarInt());
        Assert.Equal("Alice", stream.ReadVarString());
    }
}
