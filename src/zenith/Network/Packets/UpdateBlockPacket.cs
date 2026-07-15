using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

class UpdateBlockPacket : DataPacket
{
    public const int FlagNeighbors = 1;
    public const int FlagNetwork = 2;
    public const int FlagNeighborsAndNetwork = FlagNeighbors | FlagNetwork; // 3 — default wire

    public override int Id => (int)ProtocolInfo.UPDATE_BLOCK_PACKET;

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int BlockRuntimeId { get; set; }
    public int Flags { get; set; } = FlagNeighborsAndNetwork;
    public int DataLayerId { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(X);
        writer.WriteVarInt(Y);
        writer.WriteVarInt(Z);
        writer.WriteUnsignedVarInt(BlockRuntimeId);
        writer.WriteUnsignedVarInt(Flags);
        writer.WriteUnsignedVarInt(DataLayerId);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
