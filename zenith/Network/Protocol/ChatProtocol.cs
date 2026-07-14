using Zenith.Network.Session;

namespace Zenith.Network.Protocol;

/// <summary>Stub: intenções de chat. Sem TextPacket neste milestone — sem no-op público.</summary>
sealed class ChatProtocol
{
    // Session guardada para métodos futuros; stub não mantém estado de gameplay.
    private readonly NetworkSession _session;

    public ChatProtocol(NetworkSession session) => _session = session;
}
