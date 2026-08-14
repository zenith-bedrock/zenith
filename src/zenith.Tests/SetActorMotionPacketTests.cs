using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIII-B — wire-shape proof for the knockback impulse packet, independent of any gameplay
/// system. Field order/types cross-checked against gophertunnel's <c>packet.SetActorMotion.Marshal</c>
/// for protocol 2168: unsigned varlong runtime id, Velocity as three raw little-endian float32 (NOT
/// varint-encoded, unlike most positions elsewhere in the protocol), unsigned varint64 tick.
/// </summary>
public sealed class SetActorMotionPacketTests
{
    [Fact]
    public void Encodes_id_actor_runtime_id_then_three_raw_little_endian_floats_then_tick()
    {
        var packet = new SetActorMotionPacket
        {
            ActorRuntimeId = 7,
            VelocityX = 0.28f,
            VelocityY = 0.4f,
            VelocityZ = -0.28f,
            Tick = 123
        };
        var stream = new BinaryStream(packet.Encode().ToArray());

        Assert.Equal((int)ProtocolInfo.SET_ACTOR_MOTION_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(7UL, (ulong)stream.ReadUnsignedVarLong());
        Assert.Equal(0.28f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(0.4f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(-0.28f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.Equal(123UL, (ulong)stream.ReadUnsignedVarLong());
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void Sits_between_set_actor_data_and_set_actor_link_in_the_gophertunnel_id_table()
    {
        // Confirmed against gophertunnel's iota packet id sequence: SetActorData=39(0x27),
        // SetActorMotion=40(0x28), SetActorLink=41(0x29).
        Assert.Equal(0x27, (int)ProtocolInfo.SET_ACTOR_DATA_PACKET);
        Assert.Equal(0x28, (int)ProtocolInfo.SET_ACTOR_MOTION_PACKET);
        Assert.Equal(0x29, (int)ProtocolInfo.SET_ACTOR_LINK_PACKET);
    }
}
