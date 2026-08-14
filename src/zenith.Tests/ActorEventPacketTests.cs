using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIII — wire-shape proof for the hurt/death feedback packet, independent of any gameplay
/// system. Field order/types cross-checked against gophertunnel's <c>packet.ActorEvent.Marshal</c>
/// for protocol 2168: unsigned varlong runtime id, byte event id, zigzag varint32 event data,
/// optional Vec3 fire-at position.
/// </summary>
public sealed class ActorEventPacketTests
{
    [Fact]
    public void Encodes_hurt_event_as_id_actor_runtime_id_event_type_event_data_then_no_fire_position()
    {
        var packet = new ActorEventPacket { ActorRuntimeId = 42, EventId = ActorEventPacket.EventHurt, EventData = 0 };
        var encoded = packet.Encode();
        var stream = new BinaryStream(encoded.ToArray());

        Assert.Equal((int)ProtocolInfo.ACTOR_EVENT_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(42UL, (ulong)stream.ReadUnsignedVarLong());
        Assert.Equal(ActorEventPacket.EventHurt, stream.ReadByte());
        Assert.Equal(0, stream.ReadVarInt());
        Assert.False(stream.ReadBool()); // no FireAtPosition
    }

    [Fact]
    public void Death_event_id_is_distinct_from_hurt()
    {
        Assert.NotEqual(ActorEventPacket.EventHurt, ActorEventPacket.EventDeath);
        // Matches gophertunnel's ActorEventHurt=2 / ActorEventDeath=3 (iota+1 from ActorEventJump=1).
        Assert.Equal(2, ActorEventPacket.EventHurt);
        Assert.Equal(3, ActorEventPacket.EventDeath);
    }

    [Fact]
    public void Arm_swing_event_id_matches_the_bedrock_actor_event_table()
    {
        // Id 4 in both references: bedrock-protocol PHP names it ARM_SWING, gophertunnel names the
        // same numeric id ActorEventStartAttacking — same wire value, different naming convention.
        Assert.Equal(4, ActorEventPacket.EventArmSwing);
    }

    [Fact]
    public void Uses_the_corrected_actor_event_packet_id_not_the_stale_set_actor_link_id()
    {
        // Phase XXIII fix: 0x1b belongs to ActorEvent, not SetActorLink — see ProtocolInfo.cs.
        Assert.Equal(0x1b, (int)ProtocolInfo.ACTOR_EVENT_PACKET);
        Assert.Equal(0x29, (int)ProtocolInfo.SET_ACTOR_LINK_PACKET);
    }
}
