using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>SetCommandsEnabled (0x3B) — whether the client may use commands (captured: one bool).</summary>
[GamePacket((int)ProtocolInfo.SET_COMMANDS_ENABLED_PACKET)]
sealed partial class SetCommandsEnabledPacket : DataPacket
{
    [Wire]
    public bool Enabled { get; set; }
}
