using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// ActorEvent (0x1b) — server → peers only, entity-specific one-shot events (Phase XXIII). Field
/// order/types cross-checked against gophertunnel's <c>packet.ActorEvent.Marshal</c> for protocol
/// 2168: EntityRuntimeID (unsigned varlong), EventType (byte), EventData (zigzag varint32),
/// optional FireAtPosition (presence bool + Vec3, unused by every event Zenith currently emits).
/// Event id constants are the subset Zenith actually sends; see gophertunnel's
/// <c>packet.ActorEventHurt</c>/<c>ActorEventDeath</c> etc. for the full vanilla table.
/// </summary>
sealed class ActorEventPacket : DataPacket
{
    public const byte EventHurt = 2;
    public const byte EventDeath = 3;
    /// <summary>
    /// Phase XXIII-B — mob attack-swing animation. <c>AnimatePacket</c> is documented in
    /// gophertunnel as player-only ("sent... from one player to all viewers of that player");
    /// PocketMine's <c>ArmSwingAnimation</c> confirms non-player <c>Living</c> entities use this
    /// ActorEvent instead (event id 4, matching bedrock-protocol's <c>ArmSwingAnimation</c>).
    /// </summary>
    public const byte EventArmSwing = 4;

    public override int Id => (int)ProtocolInfo.ACTOR_EVENT_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public byte EventId { get; set; }
    public int EventData { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteByte(EventId);
        writer.WriteVarInt(EventData);
        writer.WriteBool(false); // FireAtPosition — not present; every event Zenith sends uses the entity's own position.
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        // Server-authoritative; no incoming dispatch registers this id.
    }
}
