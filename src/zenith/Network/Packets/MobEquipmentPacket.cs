using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>
/// MobEquipment (0x1f) — Decode mínimo do hotbar slot. Encode não usado nesta tranche.
/// </summary>
sealed class MobEquipmentPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.MOB_EQUIPMENT_PACKET;

    public long ActorRuntimeId { get; set; }
    public int InventorySlot { get; set; }
    public int HotbarSlot { get; set; }
    public int WindowId { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        ActorRuntimeId = stream.ReadUnsignedVarLong();
        SkipNetworkItem(ref stream);
        InventorySlot = stream.ReadByte();
        HotbarSlot = stream.ReadByte();
        WindowId = stream.ReadByte();
    }

    private static void SkipNetworkItem(ref BinaryStream stream)
    {
        stream.ReadShort(BinaryStream.Endianess.Little);
        stream.ReadUShort(BinaryStream.Endianess.Little);
        stream.ReadUnsignedVarInt();
        if (stream.ReadBool())
        {
            stream.ReadUnsignedVarInt();
            stream.ReadVarInt();
        }

        stream.ReadUnsignedVarInt();
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0) stream.ReadSpan(extraLen);
    }
}
