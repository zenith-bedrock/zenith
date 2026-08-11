using Zenith.Server;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Stream;

namespace Zenith.Session;

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
    private readonly ServerContext _context;
    private readonly Dictionary<RakNetSession, NetworkSession> _sessions = new();
    private readonly object _gate = new();
    private bool _accepting = true;

    public ZenithSessionListener(ServerContext context) => _context = context;

    public void OnSessionOpen(RakNetSession rakSession)
    {
        var reject = false;
        lock (_gate)
        {
            if (!_accepting)
                reject = true;
            else
                _sessions[rakSession] = new NetworkSession(rakSession, new LoginSessionHandler(), _context);
        }

        // Disconnect synchronously invokes OnSessionClose; never do that while holding _gate.
        if (reject)
            rakSession.Disconnect(DisconnectReason.ServerDisconnect);
    }

    public void OnSessionClose(RakNetSession rakSession, DisconnectReason reason)
    {
        NetworkSession session;
        lock (_gate)
        {
            if (!_sessions.Remove(rakSession, out session!)) return;
        }
        session.HandleClose(reason);
    }

    /// <summary>
    /// Kick every open session with a Bedrock DisconnectPacket, then close transport (§41).
    /// Snapshot under lock so Close callbacks can remove entries safely.
    /// </summary>
    public void DisconnectAll(string message)
    {
        NetworkSession[] snapshot;
        lock (_gate)
            snapshot = _sessions.Values.ToArray();

        foreach (var session in snapshot)
            session.DisconnectWithMessage(message);
    }

    /// <summary>Stops new gameplay sessions and packet dispatch before transport shutdown.</summary>
    public void StopAccepting()
    {
        lock (_gate)
            _accepting = false;
    }

    public bool HandleGamePacket(RakNetSession rakSession, ref BinaryStream stream)
    {
        NetworkSession? session;
        lock (_gate)
        {
            if (!_accepting)
            {
                stream.Dispose();
                return false;
            }
            _sessions.TryGetValue(rakSession, out session);
        }

        if (session is null)
        {
            // Não deveria acontecer (OnSessionOpen roda antes de qualquer game packet chegar),
            // mas não custa ser defensivo em vez de derrubar a conexão inteira.
            stream.Dispose();
            return false;
        }

        return session.HandleGamePacket(ref stream);
    }
}
