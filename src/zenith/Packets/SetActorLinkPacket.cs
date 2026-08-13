using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// SetActorLink (0x1b) — mount/dismount rider-vehicle link (outbound only, Phase XX). Field order
/// (EntityLink struct) cross-checked against gophertunnel/bedrock-protocol for protocol 2169:
/// RiderUniqueId, RiddenUniqueId, Type, Immediate, CausedByRider, VehicleAngularVelocity.
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
        writer.WriteVarLong(RiderUniqueId);
        writer.WriteVarLong(RiddenUniqueId);
        writer.WriteByte(LinkType);
        writer.WriteBool(Immediate);
        writer.WriteBool(CausedByRider);
        writer.WriteFloat(0f, BinaryStream.Endianess.Little); // VehicleAngularVelocity — unused, Zenith has no angular vehicle physics
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
