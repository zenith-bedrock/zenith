using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// SetActorMotion (0x28) — server → peer, an instantaneous client-authoritative velocity impulse.
/// Field order/types cross-checked against gophertunnel's <c>packet.SetActorMotion.Marshal</c> for
/// protocol 2168: EntityRuntimeID (unsigned varlong), Velocity (Vec3 — three raw little-endian
/// float32, NOT varint-encoded), Tick (unsigned varint64). Used for player-facing knockback: unlike
/// mobs (server-authoritative position, pushed directly), a player's position is client-authoritative,
/// so the server can only hand the client a velocity and let its own physics integrate it — this is
/// the same mechanism vanilla uses (Phase XXIII-B, "no real knockback" finding).
/// </summary>
sealed class SetActorMotionPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_ACTOR_MOTION_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public ulong Tick { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteFloat(VelocityX, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityY, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityZ, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarLong((long)Tick);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        // Server-authoritative; no incoming dispatch registers this id.
    }
}
