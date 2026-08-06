using Zenith.Packets.Generation;

namespace Zenith.Packets;

[GamePacket((int)ProtocolInfo.UPDATE_BLOCK_PACKET)]
sealed partial class UpdateBlockPacket : DataPacket
{
    public const int FlagNeighbors = 1;
    public const int FlagNetwork = 2;
    public const int FlagNeighborsAndNetwork = FlagNeighbors | FlagNetwork; // 3 — default wire

    [WireVar]
    public int X { get; set; }

    [WireVar]
    public int Y { get; set; }

    [WireVar]
    public int Z { get; set; }

    [WireVar(unsigned: true)]
    public int BlockRuntimeId { get; set; }

    [WireVar(unsigned: true)]
    public int Flags { get; set; } = FlagNeighborsAndNetwork;

    [WireVar(unsigned: true)]
    public int DataLayerId { get; set; }
}
