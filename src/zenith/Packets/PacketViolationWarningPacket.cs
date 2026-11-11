using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>Client diagnostic describing a malformed server packet (protocol 2193).</summary>
[GamePacket((int)ProtocolInfo.PACKET_VIOLATION_WARNING_PACKET)]
sealed partial class PacketViolationWarningPacket : DataPacket
{
    [WireVar]
    public int ViolationType { get; set; }

    [WireVar]
    public int Severity { get; set; }

    [WireVar]
    public int PacketId { get; set; }

    [WireString]
    public string Context { get; set; } = "";
}
