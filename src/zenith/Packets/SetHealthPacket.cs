using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>SetHealth (0x2A) — legacy health sync (captured BDS body: zigzag varint, 20 → <c>28</c>).</summary>
[GamePacket((int)ProtocolInfo.SET_HEALTH_PACKET)]
sealed partial class SetHealthPacket : DataPacket
{
    [WireVar]
    public int Health { get; set; }
}
