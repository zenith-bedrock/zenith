using System.Collections.Concurrent;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Binary;

namespace Zenith.Network.RakNet;

/// <summary>One RakNet peer seen as a Bedrock game transport connection.</summary>
internal sealed class RakNetGameConnection(RakNetSession session) : IGameTransportConnection
{
    /// <summary>Game batches are ordered per channel: StartGame must reach the client before LevelChunk.</summary>
    internal const Reliability GameBatchReliability = Reliability.ReliableOrdered;

    public RakNetSession Session { get; } = session;
    public string EndPoint => Session.EndPoint.ToString();
    public bool IsClosed => Session.IsClosed;
    public bool HasGameIdentity { get => Session.HasGameIdentity; set => Session.HasGameIdentity = value; }
    public TransportIdentity? TransportIdentity => null;
    public bool RequiresTransportIdentityBinding => false;
    public bool IncludesGamePacketIdentifier => true;

    public void SendGameBatch(ReadOnlyMemory<byte> batch, GameTransportPriority priority, byte orderChannel) =>
        Session.SendFrame(new Frame
        {
            Reliability = GameBatchReliability,
            OrderChannel = orderChannel,
            Buffer = batch
        }, priority == GameTransportPriority.Immediate
            ? RakNetSession.Priority.Immediate
            : RakNetSession.Priority.Normal);

    public void SendUnreliableGameBatch(ReadOnlyMemory<byte> batch) =>
        Session.SendFrame(new Frame { Reliability = Reliability.Unreliable, Buffer = batch }, RakNetSession.Priority.Normal);

    public void FlushOutgoing() => Session.FlushOutgoing();
    public void Disconnect() => Session.Disconnect();
}

/// <summary>
/// Plugs a <see cref="RakNetServer"/> into the transport-neutral server: owns the RakNet listener callbacks and
/// translates them to <see cref="IGameTransportHost"/> calls.
/// </summary>
internal sealed class RakNetTransport : IGameTransport, IRakNetSessionListener
{
    private readonly IGameTransportHost _host;
    private readonly ConcurrentDictionary<RakNetSession, RakNetGameConnection> _connections = new();
    private Task? _run;

    public RakNetTransport(IGameTransportHost host, RakNetServer server)
    {
        _host = host;
        Server = server;
        Server.SessionListener = this;
    }

    public string Name => "RakNet";

    /// <summary>The underlying server, exposed for tests and tooling that need to observe it.</summary>
    public RakNetServer Server { get; }

    public TransportStatistics Statistics => new(
        Server.ConnectionCount, Server.SentDatagrams, Server.SentBytes, Server.ReceivedDatagrams, Server.ReceivedBytes);

    public Task RunAsync(CancellationToken cancellationToken) => _run ??= Server.StartAsync();

    public void StopAccepting() { }

    public Task ShutdownAsync() => Server.ShutdownAsync();

    public void OnSessionOpen(RakNetSession session)
    {
        var connection = new RakNetGameConnection(session);
        _connections[session] = connection;
        if (!_host.TryOpen(connection))
            session.Disconnect(DisconnectReason.ServerDisconnect);
    }

    public void OnSessionClose(RakNetSession session, DisconnectReason reason)
    {
        if (_connections.TryRemove(session, out var connection))
            _host.Close(connection, Map(reason));
    }

    public bool HandleGamePacket(RakNetSession session, ref BinaryStream stream)
    {
        if (!_connections.TryGetValue(session, out var connection))
        {
            stream.Dispose();
            return false;
        }
        return _host.Receive(connection, ref stream);
    }

    private static GameTransportCloseReason Map(DisconnectReason reason) => reason switch
    {
        DisconnectReason.ServerDisconnect => GameTransportCloseReason.ServerDisconnect,
        DisconnectReason.Timeout => GameTransportCloseReason.Timeout,
        _ => GameTransportCloseReason.ClientDisconnect
    };
}
