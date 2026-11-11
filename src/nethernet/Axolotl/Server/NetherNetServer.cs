using Zenith.NetherNet.Axolotl.Negotiation;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Session;
using Zenith.NetherNet.Axolotl.Signaling;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Server;

/// <summary>
/// The tick-driven NetherNet server: signaling transports feed the negotiator, established peers become sessions
/// (port of <c>NetherNetServer</c>). The caller must invoke <see cref="Tick"/> regularly.
/// </summary>
public sealed class NetherNetServer : IServerInterface, ISignalingRegistry
{
    private readonly List<(ISignaling Signaling, bool Optional)> _signaling = [];
    private readonly INegotiator _negotiator;
    private readonly SessionManager _sessionManager;
    private readonly AddressBlockTracker _blockTracker = new();
    private readonly INetherNetLogger _logger;

    public NetherNetServer(INegotiator negotiator, IServerEventListener listener,
        Transport.ConnectionBudgetConfiguration budget, INetherNetLogger logger)
    {
        _negotiator = negotiator;
        _logger = logger;
        _sessionManager = new SessionManager(listener, budget, logger);
    }

    public ServerState State { get; private set; } = ServerState.Created;

    public bool IsRunning => State == ServerState.Running;

    public static NetherNetServer Create(ServerConfiguration configuration, IServerEventListener listener) =>
        new(new TransportNegotiator(
                configuration.IdentityProvider,
                configuration.IdentityVerifier,
                configuration.TransportFactory,
                configuration.Budget,
                configuration.GatheringTimeout,
                configuration.ChannelTimeout,
                configuration.MaxRemoteCandidates,
                configuration.MaxNegotiations,
                configuration.MaxNegotiationsPerAddress,
                configuration.Logger,
                configuration.AdvertisedAddresses),
            listener, configuration.Budget, configuration.Logger);

    /// <summary>Registers a signaling transport; optional ones may fail to start without stopping the server.</summary>
    public void AddSignaling(ISignaling signaling, bool optional = false)
    {
        if (State != ServerState.Created)
            throw new InvalidOperationException("Signaling transports must be added before the server is started");
        _signaling.Add((signaling, optional));
    }

    public void Start()
    {
        if (State != ServerState.Created)
            throw new InvalidOperationException("Server is already started");

        var context = new SignalingContext(_negotiator, _blockTracker, _logger);
        var hadSignaling = _signaling.Count > 0;
        try
        {
            foreach (var entry in _signaling.ToArray())
            {
                try
                {
                    entry.Signaling.Start(context);
                }
                catch (SignalingException e)
                {
                    if (!entry.Optional)
                        throw;
                    _logger.Warning($"Unable to start {entry.Signaling.Describe()}: {e.Message}");
                    entry.Signaling.Shutdown();
                    _signaling.Remove(entry);
                    continue;
                }
                _logger.Info("Started " + entry.Signaling.Describe());
            }

            if (hadSignaling && _signaling.Count == 0)
                throw new SignalingException("No signaling transport could be started");
        }
        catch (SignalingException)
        {
            Shutdown();
            throw;
        }

        State = ServerState.Running;
        _logger.Debug($"NetherNet server started with {_signaling.Count} signaling transport(s)");
    }

    /// <summary>Advances signaling, negotiation and sessions; <paramref name="now"/> is in seconds.</summary>
    public void Tick(double? now = null)
    {
        if (State is not (ServerState.Running or ServerState.Stopping))
            return;

        var time = now ?? Environment.TickCount64 / 1000.0;
        _blockTracker.Update(time);

        if (State == ServerState.Running)
        {
            foreach (var entry in _signaling)
                entry.Signaling.Tick(time);
            _negotiator.Tick(time);

            foreach (var address in _negotiator.TakeFloodingAddresses())
            {
                _logger.Info($"Blocking {address}: too many connections being negotiated at once");
                BlockAddress(address);
            }

            OpenEstablishedSessions();
        }

        _sessionManager.Tick(time);

        if (State == ServerState.Stopping && _sessionManager.Count == 0)
            Shutdown();
    }

    private void OpenEstablishedSessions()
    {
        var peers = _negotiator.TakeEstablished().ToList();
        while (peers.Count > 0)
        {
            var peer = peers[0];
            peers.RemoveAt(0);

            if (_blockTracker.IsBlocked(peer.Address.Ip) || (peer.ClientAddress is not null && _blockTracker.IsBlocked(peer.ClientAddress)))
            {
                _logger.Debug("Closed connection from blocked address: " + peer.Address.Ip
                    + (peer.ClientAddress is not null ? $" (client {peer.ClientAddress})" : ""));
                peer.Transport.Close();
                continue;
            }

            try
            {
                _sessionManager.Open(peer);
            }
            catch
            {
                foreach (var abandoned in peers)
                    abandoned.Transport.Close();
                throw;
            }
        }
    }

    public void SendPacket(int sessionId, ReadOnlySpan<byte> payload, Reliability reliability = Reliability.Reliable, int? receiptId = null)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session is null)
            return;

        try
        {
            session.Send(payload, reliability, receiptId);
        }
        catch (SessionException e)
        {
            _logger.Debug($"Session {sessionId} could not be sent to: {e.Message}");
        }
        catch (ArgumentException e)
        {
            // Oversized unreliable packets are dropped; an oversized reliable one means the stream cannot continue.
            if (reliability == Reliability.Unreliable)
            {
                _logger.Debug($"Dropped an unreliable packet for session {sessionId}: {e.Message}");
                return;
            }
            _logger.Debug($"Closing session {sessionId}: {e.Message}");
            session.InitiateDisconnect(DisconnectReason.SendFailed, awaitPeerClose: false);
        }
    }

    public void CloseSession(int sessionId, DisconnectReason reason = DisconnectReason.ServerDisconnect) =>
        _sessionManager.GetSession(sessionId)?.InitiateDisconnect(reason);

    /// <summary>Blocks an address for <paramref name="timeout"/> seconds (negative: until restart) and drops its sessions.</summary>
    public void BlockAddress(string address, int timeout = 300)
    {
        var now = Environment.TickCount64 / 1000.0;
        _blockTracker.Block(address, timeout < 0 ? double.PositiveInfinity : now + timeout);
        _logger.Debug($"Blocked {address}" + (timeout < 0 ? "" : $" for {timeout} seconds"));

        var blocked = InternetAddress.NormalizeIp(address) ?? address;
        foreach (var session in _sessionManager.GetSessions())
        {
            var sessionIp = session.RemoteAddress.Ip;
            if ((InternetAddress.NormalizeIp(sessionIp) ?? sessionIp) == blocked || session.ClientAddress == blocked)
                session.InitiateDisconnect(DisconnectReason.AddressBlocked, awaitPeerClose: false);
        }
    }

    public void UnblockAddress(string address)
    {
        _blockTracker.Unblock(address);
        _logger.Debug($"Unblocked {address}");
    }

    /// <summary>Stops accepting connections and disconnects sessions; <see cref="Tick"/> completes the shutdown once they close.</summary>
    public void InitiateShutdown(DisconnectReason reason = DisconnectReason.ServerShutdown)
    {
        if (State != ServerState.Running)
        {
            Shutdown();
            return;
        }

        State = ServerState.Stopping;
        StopAcceptingConnections();
        foreach (var session in _sessionManager.GetSessions())
            session.InitiateDisconnect(reason);
    }

    public void Shutdown()
    {
        if (State == ServerState.Stopped)
            return;

        var wasStopping = State == ServerState.Stopping;
        State = ServerState.Stopped;
        if (!wasStopping)
            StopAcceptingConnections();

        _sessionManager.Shutdown();
        _logger.Debug("NetherNet server shut down");
    }

    private void StopAcceptingConnections()
    {
        foreach (var entry in _signaling)
            entry.Signaling.Shutdown();
        _negotiator.Shutdown();
    }
}
