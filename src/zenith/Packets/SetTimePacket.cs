using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>Sincroniza o tempo do mundo (ticks 0..23999) com o cliente.</summary>
[GamePacket((int)ProtocolInfo.SET_TIME_PACKET)]
sealed partial class SetTimePacket : DataPacket
{
    [WireVar]
    public int Time { get; set; }
}
