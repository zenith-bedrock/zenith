using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>MobEquipment (0x1f) — selected held stack / hotbar replication.</summary>
sealed class MobEquipmentPacket : DataPacket
{
    public const byte WindowInventory = 0;
    public const byte WindowOffhand = 119;

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
        Item.WriteNetworkItemStackDescriptor(ref writer);
        writer.WriteByte((byte)InventorySlot);
        writer.WriteByte((byte)HotbarSlot);
        writer.WriteByte((byte)WindowId);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        ActorRuntimeId = stream.ReadUnsignedVarLong();
        Item = ReadNetworkItem(ref stream);
        InventorySlot = stream.ReadByte();
        HotbarSlot = stream.ReadByte();
        WindowId = stream.ReadByte();
    }

    private static NetworkItemStack ReadNetworkItem(ref BinaryStream stream)
    {
        var networkId = stream.ReadShort(BinaryStream.Endianess.Little);
        var count = stream.ReadUShort(BinaryStream.Endianess.Little);
        var meta = stream.ReadUnsignedVarInt();
        var stackNetworkId = stream.ReadBool() ? stream.ReadVarInt() : 0;
        var blockRuntimeId = stream.ReadUnsignedVarInt();
        var extraLen = stream.ReadUnsignedVarInt();
        if (extraLen > 0) stream.ReadSpan(extraLen);
        return new NetworkItemStack(networkId, count, blockRuntimeId, meta, stackNetworkId);
    }
}
