using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>PlayerHotbar (0x30) — tells the client which hotbar slot is selected (captured BDS body: <c>00 00 01</c>).</summary>
[GamePacket((int)ProtocolInfo.PLAYER_HOTBAR_PACKET)]
sealed partial class PlayerHotbarPacket : DataPacket
{
    [WireVar(unsigned: true)]
    public int SelectedSlot { get; set; }

    [Wire]
    public byte WindowId { get; set; }

    [Wire]
    public bool SelectSlot { get; set; }
}
