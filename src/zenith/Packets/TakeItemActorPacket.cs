using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>TakeItemActor (0x11) — pickup animation + despawn for viewers (outbound).</summary>
[GamePacket((int)ProtocolInfo.TAKE_ITEM_ACTOR_PACKET)]
sealed partial class TakeItemActorPacket : DataPacket
{
    [WireVar]
    public ulong ItemEntityRuntimeId { get; set; }

    [WireVar]
    public ulong TakerEntityRuntimeId { get; set; }
}
