using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>AddItemActor (0x0f) — dropped item entity (outbound). Not AddActor.</summary>
sealed class AddItemActorPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.ADD_ITEM_ACTOR_PACKET;

    public long EntityUniqueId { get; set; }
    public ulong EntityRuntimeId { get; set; }
    public NetworkItemStack Item { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public bool FromFishing { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarLong(EntityUniqueId);
        writer.WriteUnsignedVarLong((long)EntityRuntimeId);
        Item.Write(ref writer);
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityX, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityY, BinaryStream.Endianess.Little);
        writer.WriteFloat(VelocityZ, BinaryStream.Endianess.Little);
        writer.WriteUnsignedVarInt(0); // empty EntityMetadata
        writer.WriteBool(FromFishing);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
