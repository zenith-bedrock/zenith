using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>
/// TakeItemActor (0x11) — pickup animation only (outbound). Does NOT despawn the item actor —
/// confirmed against PocketMine/Dragonfly, both of which always pair this with a real actor
/// removal. A caller still needs to send <see cref="RemoveActorPacket"/> for the picked-up entity
/// or it stays rendered on the client forever (a real, previously-shipped bug — see
/// <c>FloorDropSystem</c>'s pickup loop and the Phase XXVI entry in docs/entity-fidelity.md).
/// </summary>
[GamePacket((int)ProtocolInfo.TAKE_ITEM_ACTOR_PACKET)]
sealed partial class TakeItemActorPacket : DataPacket
{
    [WireVar]
    public ulong ItemEntityRuntimeId { get; set; }

    [WireVar]
    public ulong TakerEntityRuntimeId { get; set; }
}
