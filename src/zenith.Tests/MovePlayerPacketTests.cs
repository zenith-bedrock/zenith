using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class MovePlayerPacketTests
{
    [Fact]
    public void Teleport_encode_includes_mode_and_command_cause()
    {
        var bytes = MovePlayerPacket.CreateTeleport(
            entityRuntimeId: 7,
            x: 1.5f,
            y: -60f,
            z: -2.25f,
            pitch: 10f,
            yaw: 90f,
            headYaw: 90f,
            tick: 0).Encode().ToArray();

        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.MOVE_PLAYER_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(7ul, (ulong)stream.ReadUnsignedVarLong());
        Assert.Equal(1.5f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(-60f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(-2.25f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(10f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(90f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(90f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(MovePlayerPacket.ModeTeleport, stream.ReadByte());
        Assert.True(stream.ReadBool());
        Assert.Equal(0ul, (ulong)stream.ReadUnsignedVarLong()); // ridden
        Assert.True(stream.ReadBool()); // TeleportData presence (Cereal optional, ADR §79/§88)
        Assert.Equal(MovePlayerPacket.TeleportCauseCommand, stream.ReadInt(BinaryStream.Endianess.Little));
        Assert.Equal(0, stream.ReadInt(BinaryStream.Endianess.Little)); // source entity type
        Assert.Equal(0ul, (ulong)stream.ReadUnsignedVarLong()); // tick
    }
}
