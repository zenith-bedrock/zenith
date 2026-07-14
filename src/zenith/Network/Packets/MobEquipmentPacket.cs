using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>MobEquipment (0x1f) — Decode hotbar; Encode fan-out peeld held item.</summary>
sealed class MobEquipmentPacket : DataPacket
{
    public const byte WindowInventory = 0;

    public override int Id => (int)ProtocolInfo.MOB_EQUIPMENT_PACKET;

    public long ActorRuntimeId { get; set; }
    public NetworkItemStack Item { get; set; } = NetworkItemStack.Empty;
    public int InventorySlot { get; set; }
    public int HotbarSlot { get; set; }
    public int WindowId { get; set; } = WindowInventory;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong(ActorRuntimeId);
        Item.Write(ref writer);
        writer.WriteByte((byte)InventorySlot);
        writer.WriteByte((byte)HotbarSlot);
        writer.WriteByte((byte)WindowId);
        return writer.GetBufferDisposing();
    }

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
