using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Negotiation;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Signaling.Http;
using Zenith.NetherNet.Axolotl.Signaling.Lan.Packet;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Signaling.Lan;

/// <summary>One LAN peer's negotiation and the signals still owed to it (port of <c>LanConnection</c>).</summary>
public sealed class LanConnection(INegotiation negotiation, ulong peerId, InternetAddress address, string connectionId)
{
    private bool _answerSent;

    public INegotiation Negotiation { get; } = negotiation;
    public ulong PeerId { get; } = peerId;
    public InternetAddress Address { get; } = address;
    public string ConnectionId { get; } = connectionId;

    public IReadOnlyList<Signal> TakeSignals()
    {
        if (Negotiation.IsFailed)
            return [Signal.Error(ConnectionId, Negotiation.FailureCode)];

        var signals = new List<Signal>();
        if (!_answerSent)
        {
            var answer = Negotiation.Answer;
            if (answer is null)
                return [];
            signals.Add(new Signal(SignalType.ConnectResponse, ConnectionId, answer));
            _answerSent = true;
        }

        foreach (var candidate in Negotiation.TakeLocalCandidates())
            signals.Add(new Signal(SignalType.CandidateAdd, ConnectionId, candidate));
        return signals;
    }
}

/// <summary>
/// LAN discovery and trickle-ICE signaling over UDP (port 7551), the way Bedrock finds servers on the local network
/// (port of <c>LanSignaling</c>).
/// </summary>
public sealed class LanSignaling : ISignaling
{
    public const int DefaultPort = 7551;
    public const int MaxDatagramsPerTick = 256;
    public const int DefaultMaxConnections = 64;
    public const int DefaultMaxDiscoveryRequestsPerSecond = 64;
    private const string PingData = "Ping";

    private readonly IServerInfoProvider _infoProvider;
    private readonly InternetAddress _bindAddress;
    private readonly int _maxConnections;
    private readonly int? _maxDiscoveryRequestsPerSecond;

    private DatagramSocket? _socket;
    private readonly Dictionary<string, LanConnection> _connections = new();
    private readonly Dictionary<string, int> _discoveryRequests = new();
    private double _discoveryRequestsExpiry;
    private SignalingState _state = SignalingState.Created;
    private INegotiator _negotiator = null!;
    private IAddressBlockList _blockList = null!;
    private INetherNetLogger _logger = NullNetherNetLogger.Instance;

    public LanSignaling(IServerInfoProvider infoProvider, ulong networkId, InternetAddress? bindAddress = null,
        int maxConnections = DefaultMaxConnections, int? maxDiscoveryRequestsPerSecond = DefaultMaxDiscoveryRequestsPerSecond)
    {
        if (networkId == 0)
            throw new ArgumentException("Network id must not be zero");
        if (maxConnections < 1)
            throw new ArgumentException($"Maximum connections must be positive, got {maxConnections}");
        if (maxDiscoveryRequestsPerSecond is < 1)
            throw new ArgumentException($"Maximum discovery requests per second must be positive or null, got {maxDiscoveryRequestsPerSecond}");

        _infoProvider = infoProvider;
        NetworkId = networkId;
        _bindAddress = bindAddress ?? new InternetAddress("0.0.0.0", DefaultPort, 4);
        _maxConnections = maxConnections;
        _maxDiscoveryRequestsPerSecond = maxDiscoveryRequestsPerSecond;
    }

    public ulong NetworkId { get; }

    public InternetAddress BoundAddress =>
        _socket?.BindAddress ?? throw new InvalidOperationException("LAN signaling is not listening");

    public string Describe() => "LAN signaling on " + (_socket?.BindAddress ?? _bindAddress);

    public void Start(SignalingContext context)
    {
        _state.CheckCanStart();

        try
        {
            _socket = new DatagramSocket(_bindAddress, broadcast: true);
        }
        catch (Utils.SocketException e)
        {
            throw new SignalingException(e.Message, e);
        }

        _negotiator = context.Negotiator;
        _blockList = context.BlockList;
        _logger = new PrefixedNetherNetLogger(context.Logger, "LAN signaling");
        _state = SignalingState.Started;
        _logger.Debug($"Listening on {_socket.BindAddress} as network id {NetworkId}");
    }

    public void Tick(double now)
    {
        var socket = _socket;
        if (_state != SignalingState.Started || socket is null)
            return;

        if (now >= _discoveryRequestsExpiry)
        {
            _discoveryRequests.Clear();
            _discoveryRequestsExpiry = now + 1.0;
        }

        ReceiveAll(socket);
        ProcessConnections();
    }

    public void Shutdown()
    {
        if (_state == SignalingState.ShutDown)
            return;
        _state = SignalingState.ShutDown;

        foreach (var connection in _connections.Values)
            connection.Negotiation.Fail("LAN signaling is shutting down", ErrorCode.NoSignalingChannel);
        _connections.Clear();

        _socket?.Dispose();
        _socket = null;
    }

    private void ReceiveAll(DatagramSocket socket)
    {
        for (var i = 0; i < MaxDatagramsPerTick; i++)
        {
            (byte[] Datagram, InternetAddress From)? received;
            try
            {
                received = socket.Receive();
            }
            catch (Utils.SocketException e)
            {
                _logger.Warning(e.Message);
                return;
            }
            if (received is null)
                return;

            var (datagram, address) = received.Value;
            if (_blockList.IsBlocked(address.Ip))
                continue;

            try
            {
                HandleDatagram(datagram, address);
            }
            catch (SignalingException e)
            {
                _logger.Debug($"Ignoring a datagram from {address}: {e.Message}");
            }
        }
    }

    private void HandleDatagram(byte[] datagram, InternetAddress address)
    {
        var packet = PacketSerializer.Decode(datagram);
        // Our own broadcasts come back to us.
        if (packet.SenderId == NetworkId)
            return;

        switch (packet)
        {
            case RequestPacket:
            {
                var requests = _discoveryRequests[address.Ip] = _discoveryRequests.GetValueOrDefault(address.Ip) + 1;
                if (_maxDiscoveryRequestsPerSecond is not null && requests > _maxDiscoveryRequestsPerSecond)
                    return;
                var info = _infoProvider.GetServerInfo();
                if (info is not null)
                    Send(new ResponsePacket(NetworkId, ServerInfoCodec.Encode(info)), address);
                break;
            }
            case MessagePacket message:
                HandleMessage(message, address);
                break;
        }
    }

    private void HandleMessage(MessagePacket packet, InternetAddress address)
    {
        if (packet.RecipientId != NetworkId)
            return;
        // Keep-alives carry no signal.
        if (packet.Data is "" or PingData)
            return;

        var signal = Signal.Parse(packet.Data);
        var senderId = packet.SenderId;
        var key = $"{senderId}/{signal.ConnectionId}";

        switch (signal.Type)
        {
            case SignalType.ConnectRequest:
                HandleOffer(signal, senderId, key, address);
                break;
            case SignalType.CandidateAdd:
                HandleCandidate(signal, key);
                break;
            case SignalType.ConnectError:
                HandlePeerError(signal, key);
                break;
        }
    }

    private void HandleOffer(Signal signal, ulong senderId, string key, InternetAddress address)
    {
        if (_connections.ContainsKey(key))
            return;
        if (CountNegotiating() >= _maxConnections)
        {
            _logger.Debug($"Ignored an offer from {senderId}: connection limit of {_maxConnections} reached");
            return;
        }

        INegotiation negotiation;
        try
        {
            negotiation = _negotiator.BeginNegotiation(signal.Data, senderId.ToString(), CandidateMode.Trickle);
        }
        catch (NegotiationException e)
        {
            _logger.Debug($"Refused an offer from {senderId}: {e.Message}");
            SendSignal(senderId, Signal.Error(signal.ConnectionId, e.ErrorCode), address);
            return;
        }

        _connections[key] = new LanConnection(negotiation, senderId, address, signal.ConnectionId);
    }

    private void HandleCandidate(Signal signal, string key)
    {
        if (!_connections.TryGetValue(key, out var connection))
            return;
        try
        {
            connection.Negotiation.AddRemoteCandidate(signal.Data);
        }
        catch (NegotiationException e)
        {
            _logger.Debug($"Peer sent an unusable ICE candidate: {e.Message}");
        }
    }

    private void HandlePeerError(Signal signal, string key)
    {
        if (!_connections.TryGetValue(key, out var connection) || connection.Negotiation.IsFinished)
            return;
        _connections.Remove(key);
        connection.Negotiation.Fail("Peer reported error code " + signal.Data, ErrorCode.GenericFailure);
    }

    private int CountNegotiating() => _connections.Values.Count(c => !c.Negotiation.IsFinished);

    private void ProcessConnections()
    {
        foreach (var (key, connection) in _connections.ToArray())
        {
            foreach (var signal in connection.TakeSignals())
                SendSignal(connection.PeerId, signal, connection.Address);

            if (connection.Negotiation.IsClosed)
                _connections.Remove(key);
        }
    }

    private void SendSignal(ulong peerId, Signal signal, InternetAddress address) =>
        Send(new MessagePacket(NetworkId, peerId, signal.ToString()), address);

    private void Send(Packet.Packet packet, InternetAddress address)
    {
        try
        {
            _socket?.Send(PacketSerializer.Encode(packet), address);
        }
        catch (Exception e) when (e is Utils.SocketException or DiscoveryException)
        {
            _logger.Debug($"Could not send to {address}: {e.Message}");
        }
    }
}
