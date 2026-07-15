using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Sent by the client once it has finished processing the spawn sequence (after the
/// PlayStatus PLAYER_SPAWN status), confirming its local player entity is ready. This is the
/// real signal that the client left the loading screen and the session can switch to the
/// final in-game handler.
/// </summary>
class SetLocalPlayerAsInitializedPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_LOCAL_PLAYER_AS_INITIALIZED_PACKET;

    public long ActorRuntimeId { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        ActorRuntimeId = stream.ReadUnsignedVarLong();
    }
}
