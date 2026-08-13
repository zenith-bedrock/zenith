using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// MobArmorEquipment (0x20) — server → peers only, visual armor on another actor (Phase XI.2).
/// Zenith never receives this from a client; Decode is a defensive no-op.
/// </summary>
sealed class MobArmorEquipmentPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.MOB_ARMOR_EQUIPMENT_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public NetworkItemStack Head { get; set; } = NetworkItemStack.Empty;
    public NetworkItemStack Torso { get; set; } = NetworkItemStack.Empty;
    public NetworkItemStack Legs { get; set; } = NetworkItemStack.Empty;
    public NetworkItemStack Feet { get; set; } = NetworkItemStack.Empty;
    public NetworkItemStack Body { get; set; } = NetworkItemStack.Empty;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        Head.WriteNetworkItemStackDescriptor(ref writer);
        Torso.WriteNetworkItemStackDescriptor(ref writer);
        Legs.WriteNetworkItemStackDescriptor(ref writer);
        Feet.WriteNetworkItemStackDescriptor(ref writer);
        Body.WriteNetworkItemStackDescriptor(ref writer);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        // Server-authoritative armor; no incoming dispatch registers this id.
    }
}
