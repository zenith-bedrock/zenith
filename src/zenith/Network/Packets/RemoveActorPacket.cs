using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>RemoveActor (0x0e). uniqueId = RuntimeId.</summary>
class RemoveActorPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.REMOVE_ACTOR_PACKET;

    public long ActorUniqueId { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarLong(ActorUniqueId);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
