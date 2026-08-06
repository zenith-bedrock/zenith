using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>SetPlayerGameType (0x3E) — runtime player game mode (ADR §52).</summary>
[GamePacket((int)ProtocolInfo.SET_PLAYER_GAME_TYPE_PACKET)]
sealed partial class SetPlayerGameTypePacket : DataPacket
{
    [WireVar]
    public int GameType { get; set; }
}
