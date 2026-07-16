using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>InventoryContent (0x31) — sync full window contents to client.</summary>
sealed class InventoryContentPacket : DataPacket
{
    public const int WindowInventory = 0;
    public const int WindowChest = 2;
    public const int WindowUI = 124;

    public override int Id => (int)ProtocolInfo.INVENTORY_CONTENT_PACKET;

    public int WindowId { get; set; } = WindowInventory;
    public NetworkItemStack[] Slots { get; set; } = [];

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarInt(WindowId);
        writer.WriteUnsignedVarInt(Slots.Length);
        foreach (var slot in Slots)
            slot.Write(ref writer);

        writer.WriteByte(0); // FullContainerName.container_id
        writer.WriteBool(false); // no dynamic id
        NetworkItemStack.Empty.Write(ref writer); // storage
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
