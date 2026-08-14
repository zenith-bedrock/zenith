using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// SetActorLink (0x29) — mount/dismount rider-vehicle link (outbound only, Phase XX). Field order
/// (EntityLink struct) cross-checked against gophertunnel's <c>entity_link.go</c>
/// (<c>EntityLink.Marshal</c>) for protocol 2168: RiddenUniqueId, RiderUniqueId, Type, Immediate,
/// CausedByRider, VehicleAngularVelocity — the RIDDEN (vehicle) unique id is written FIRST, then the
/// RIDER (passenger). Phase XXIII-B cross-reference review found Zenith had these swapped (wrote
/// Rider first) despite this file's own prior comment claiming the opposite order was verified —
/// every mount/dismount link Zenith has ever sent (Minecart riding) was silently backwards on the
/// wire. Packet id corrected in Phase XXIII — was wired to 0x1b, which is actually ActorEvent's id;
/// see ProtocolInfo.SET_ACTOR_LINK_PACKET.
/// </summary>
sealed class SetActorLinkPacket : DataPacket
{
    public const byte TypeRemove = 0;
    public const byte TypeRider = 1;
    public const byte TypePassenger = 2;

    public override int Id => (int)ProtocolInfo.SET_ACTOR_LINK_PACKET;

    public long RiderUniqueId { get; set; }
    public long RiddenUniqueId { get; set; }
    public byte LinkType { get; set; }
    public bool Immediate { get; set; }
    public bool CausedByRider { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarLong(RiddenUniqueId);
        writer.WriteVarLong(RiderUniqueId);
        writer.WriteByte(LinkType);
        writer.WriteBool(Immediate);
        writer.WriteBool(CausedByRider);
        writer.WriteFloat(0f, BinaryStream.Endianess.Little); // VehicleAngularVelocity — unused, Zenith has no angular vehicle physics
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
