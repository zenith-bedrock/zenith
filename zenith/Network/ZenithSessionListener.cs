using zenith.Session;
using zenith.Session.Handler;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Stream;

namespace zenith.Network;

/// <summary>
/// Ponte entre o <see cref="RakNetSession"/> (transporte) e o <see cref="NetworkSession"/>
/// (protocolo Bedrock). Essa é a única classe do lado do jogo que deveria tocar em raknet
/// diretamente; todo o resto trabalha em cima de NetworkSession.
///
/// Agora que o raknet expõe OnSessionOpen/OnSessionClose, o ciclo de vida da NetworkSession
/// é determinístico: criada no open, removida no close. Nada de depender do GC.
/// </summary>
class ZenithSessionListener : IRakNetSessionListener
{
    private readonly Dictionary<RakNetSession, NetworkSession> _sessions = new();

    public void OnSessionOpen(RakNetSession rakSession)
    {
        _sessions[rakSession] = new NetworkSession(rakSession, new LoginSessionHandler());
    }

    public void OnSessionClose(RakNetSession rakSession, DisconnectReason reason)
    {
        if (!_sessions.Remove(rakSession, out var session)) return;
        session.HandleClose(reason);
    }

    public bool HandleGamePacket(RakNetSession rakSession, BinaryStream stream)
    {
        if (!_sessions.TryGetValue(rakSession, out var session))
        {
            // Não deveria acontecer (OnSessionOpen roda antes de qualquer game packet chegar),
            // mas não custa ser defensivo em vez de derrubar a conexão inteira.
            stream.Dispose();
            return false;
        }

        return session.HandleGamePacket(stream);
    }
}
