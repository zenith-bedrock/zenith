using Zenith.Server;
using Zenith.Session.Handler;
using Zenith.NetherNet;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Stream;

namespace Zenith.Session;

/// <summary>Adapts RakNet and NetherNet peers to the same Bedrock session lifecycle.</summary>
class ZenithSessionListener : IRakNetSessionListener
{
    private readonly ServerContext _context;
    private readonly Dictionary<IGameTransportConnection, NetworkSession> _sessions = new();
    private readonly Dictionary<RakNetSession, RakNetGameTransportConnection> _raknet = new();
    private readonly Dictionary<NetherNetConnection, NetherNetGameTransportConnection> _nethernet = new();
    private readonly object _gate = new();
    private bool _accepting = true;

    public ZenithSessionListener(ServerContext context) => _context = context;

    public void OnSessionOpen(RakNetSession rakSession)
    {
        var transport = new RakNetGameTransportConnection(rakSession);
        lock (_gate)
            _raknet[rakSession] = transport;
        if (!Open(transport))
            rakSession.Disconnect(DisconnectReason.ServerDisconnect);
    }

    public void OnSessionClose(RakNetSession rakSession, DisconnectReason reason)
    {
        RakNetGameTransportConnection? transport;
        lock (_gate)
            _raknet.Remove(rakSession, out transport);
        if (transport is not null)
            Close(transport, reason);
    }

    public void OnNetherNetConnectionOpened(NetherNetConnection connection)
    {
        var transport = new NetherNetGameTransportConnection(connection);
        var reject = false;
        lock (_gate)
        {
            if (!_accepting)
                reject = true;
            else
            {
                _sessions[transport] = new NetworkSession(transport, new LoginSessionHandler(), _context);
                _nethernet[connection] = transport;
            }
        }
        if (reject)
        {
            connection.Disconnect("Server is shutting down.");
            return;
        }
        connection.GameBatchReceived += OnNetherNetGameBatchReceived;
        connection.Closed += OnNetherNetConnectionClosed;
        if (connection.IsClosed)
            Close(transport, DisconnectReason.ClientDisconnect);
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

    public bool HandleGamePacket(RakNetSession rakSession, ref BinaryStream stream)
    {
        RakNetGameTransportConnection? transport;
        lock (_gate)
            _raknet.TryGetValue(rakSession, out transport);
        if (transport is null)
        {
            stream.Dispose();
            return false;
        }

        return HandleGameBatch(transport, ref stream);
    }

    private bool Open(IGameTransportConnection transport)
    {
        lock (_gate)
        {
            if (!_accepting)
                return false;
            _sessions[transport] = new NetworkSession(transport, new LoginSessionHandler(), _context);
            return true;
        }
    }

    private void Close(IGameTransportConnection transport, DisconnectReason reason)
    {
        NetworkSession? session;
        lock (_gate)
        {
            _sessions.Remove(transport, out session);
            if (transport is NetherNetGameTransportConnection netherNet)
                _nethernet.Remove(netherNet.Connection);
        }
        session?.HandleClose(reason);
    }

    private bool HandleGameBatch(IGameTransportConnection transport, ref BinaryStream stream)
    {
        NetworkSession? session;
        lock (_gate)
        {
            if (!_accepting)
            {
                stream.Dispose();
                return false;
            }
            _sessions.TryGetValue(transport, out session);
        }

        if (session is null)
        {
            stream.Dispose();
            return false;
        }
        return session.HandleGamePacket(ref stream);
    }

    private void OnNetherNetGameBatchReceived(NetherNetConnection connection, byte[] batch)
    {
        NetherNetGameTransportConnection? transport;
        lock (_gate)
            _nethernet.TryGetValue(connection, out transport);
        if (transport is null)
            return;
        var stream = new BinaryStream(batch);
        HandleGameBatch(transport, ref stream);
    }

    private void OnNetherNetConnectionClosed(NetherNetConnection connection, string reason)
    {
        NetherNetGameTransportConnection? transport;
        lock (_gate)
            _nethernet.TryGetValue(connection, out transport);
        if (transport is not null)
        {
            var disconnectReason = reason.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ||
                                   reason.Contains("server", StringComparison.OrdinalIgnoreCase)
                ? DisconnectReason.ServerDisconnect
                : DisconnectReason.ClientDisconnect;
            Close(transport, disconnectReason);
        }
    }
}
