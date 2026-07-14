using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>ContainerOpen (0x2e) — abre UI de inventário do jogador (window 0).</summary>
sealed class ContainerOpenPacket : DataPacket
{
    public const byte WindowTypeInventory = 0xff;

    public override int Id => (int)ProtocolInfo.CONTAINER_OPEN_PACKET;

    public byte WindowId { get; set; }
    public byte WindowType { get; set; } = WindowTypeInventory;
    public int BlockX { get; set; }
    public int BlockY { get; set; }
    public int BlockZ { get; set; }
    public long ActorUniqueId { get; set; } = -1;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteByte(WindowId);
        writer.WriteByte(WindowType);
        writer.WriteVarInt(BlockX);
        writer.WriteVarInt(BlockY);
        writer.WriteVarInt(BlockZ);
        writer.WriteVarLong(ActorUniqueId);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
