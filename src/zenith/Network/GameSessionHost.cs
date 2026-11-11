using Zenith.Binary;
using Zenith.Server;
using Zenith.Session;
using Zenith.Session.Handler;

namespace Zenith.Network;

/// <summary>
/// Binds transport connections to Bedrock <see cref="NetworkSession"/>s. This is the only place that knows both
/// sides: transports talk to it through <see cref="IGameTransportHost"/>, sessions only see
/// <see cref="IGameTransportConnection"/>.
/// </summary>
internal sealed class GameSessionHost(ServerContext context) : IGameTransportHost
{
    private readonly Dictionary<IGameTransportConnection, NetworkSession> _sessions = new();
    private readonly object _gate = new();
    private bool _accepting = true;

    public bool TryOpen(IGameTransportConnection connection)
    {
        lock (_gate)
        {
            if (!_accepting)
                return false;
            _sessions[connection] = new NetworkSession(connection, new LoginSessionHandler(), context);
            return true;
        }
    }

    public bool Receive(IGameTransportConnection connection, ref BinaryStream batch)
    {
        NetworkSession? session;
        lock (_gate)
        {
            if (!_accepting)
            {
                batch.Dispose();
                return false;
            }
            _sessions.TryGetValue(connection, out session);
        }

        if (session is null)
        {
            batch.Dispose();
            return false;
        }
        return session.HandleGamePacket(ref batch);
    }

    public void Close(IGameTransportConnection connection, GameTransportCloseReason reason)
    {
        NetworkSession? session;
        lock (_gate)
            _sessions.Remove(connection, out session);
        session?.HandleClose(reason);
    }

    /// <summary>Kicks each Bedrock session before closing its transport (§41).</summary>
    public void DisconnectAll(string message)
    {
        NetworkSession[] snapshot;
        lock (_gate)
            snapshot = _sessions.Values.ToArray();

        foreach (var session in snapshot)
            session.DisconnectWithMessage(message);
    }

    public void StopAccepting()
    {
        lock (_gate)
            _accepting = false;
    }
}
