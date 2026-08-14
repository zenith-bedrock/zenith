using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIII-B cross-reference review — the RiderUniqueId/RiddenUniqueId fields were swapped on
/// the wire (writer wrote Rider first; gophertunnel's <c>EntityLink.Marshal</c> writes Ridden
/// first). Every mount/dismount link Zenith ever sent (Minecart riding) was backwards. This pins the
/// corrected field order independent of any gameplay system.
/// </summary>
public sealed class SetActorLinkPacketTests
{
    [Fact]
    public void Encodes_ridden_unique_id_before_rider_unique_id()
    {
        var packet = new SetActorLinkPacket
        {
            RiderUniqueId = 111,
            RiddenUniqueId = 222,
            LinkType = SetActorLinkPacket.TypeRider,
            Immediate = true,
            CausedByRider = true
        };
        var stream = new BinaryStream(packet.Encode().ToArray());

        Assert.Equal((int)ProtocolInfo.SET_ACTOR_LINK_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(222L, stream.ReadVarLong()); // Ridden (vehicle) first.
        Assert.Equal(111L, stream.ReadVarLong()); // Rider (passenger) second.
        Assert.Equal(SetActorLinkPacket.TypeRider, stream.ReadByte());
        Assert.True(stream.ReadBool());
        Assert.True(stream.ReadBool());
        Assert.Equal(0f, stream.ReadFloat(BinaryStream.Endianess.Little));
        Assert.True(stream.IsEndOfFile);
    }
}
