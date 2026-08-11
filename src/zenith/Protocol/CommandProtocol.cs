using Zenith.Packets;
using Zenith.Session;

namespace Zenith.Protocol;

/// <summary>Transmits already-projected Bedrock command metadata.</summary>
sealed class CommandProtocol
{
    private readonly NetworkSession _session;
    public CommandProtocol(NetworkSession session) => _session = session;
    public void SendAvailableCommands(AvailableCommandsPacket packet) => _session.SendDataPacket(packet);
}
