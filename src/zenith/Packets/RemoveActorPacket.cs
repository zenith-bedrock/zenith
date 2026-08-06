using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>RemoveActor (0x0e). uniqueId = RuntimeId.</summary>
[GamePacket((int)ProtocolInfo.REMOVE_ACTOR_PACKET)]
sealed partial class RemoveActorPacket : DataPacket
{
    [WireVar]
    public long ActorUniqueId { get; set; }
}
