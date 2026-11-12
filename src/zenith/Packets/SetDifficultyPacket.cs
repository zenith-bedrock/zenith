using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>SetDifficulty (0x3C) — world difficulty, sent by BDS right after StartGame (captured: body is one unsigned varint).</summary>
[GamePacket((int)ProtocolInfo.SET_DIFFICULTY_PACKET)]
sealed partial class SetDifficultyPacket : DataPacket
{
    [WireVar(unsigned: true)]
    public int Difficulty { get; set; }
}
