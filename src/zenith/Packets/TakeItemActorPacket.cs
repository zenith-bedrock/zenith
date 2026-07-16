using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>TakeItemActor (0x11) — pickup animation + despawn for viewers (outbound).</summary>
sealed class TakeItemActorPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.TAKE_ITEM_ACTOR_PACKET;

    public ulong ItemEntityRuntimeId { get; set; }
    public ulong TakerEntityRuntimeId { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ItemEntityRuntimeId);
        writer.WriteUnsignedVarLong((long)TakerEntityRuntimeId);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
