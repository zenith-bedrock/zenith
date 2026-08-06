using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>
/// Sent by the client once it has finished processing the spawn sequence (after the
/// PlayStatus PLAYER_SPAWN status), confirming its local player entity is ready. This is the
/// real signal that the client left the loading screen and the session can switch to the
/// final in-game handler.
/// </summary>
[GamePacket((int)ProtocolInfo.SET_LOCAL_PLAYER_AS_INITIALIZED_PACKET)]
sealed partial class SetLocalPlayerAsInitializedPacket : DataPacket
{
    [WireVar]
    public ulong ActorRuntimeId { get; set; }
}
