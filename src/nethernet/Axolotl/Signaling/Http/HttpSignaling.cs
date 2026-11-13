using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Signaling.Http;

/// <summary>Prefixes every message with a component name (port of PocketMine's <c>PrefixedLogger</c>).</summary>
public sealed class PrefixedNetherNetLogger(INetherNetLogger inner, string prefix) : INetherNetLogger
{
    public bool IsDebugEnabled => inner.IsDebugEnabled;
    public void Debug(string message) => inner.Debug($"[{prefix}] {message}");
    public void Info(string message) => inner.Info($"[{prefix}] {message}");
    public void Warning(string message) => inner.Warning($"[{prefix}] {message}");
    public void Error(string message) => inner.Error($"[{prefix}] {message}");
}

/// <summary>A non-blocking listening TCP socket (port of <c>TcpServerSocket</c>).</summary>
public sealed class TcpServerSocket : IDisposable
{
    private const int Backlog = 128;
    private readonly Socket _socket;
    private bool _closed;

    public InternetAddress BindAddress { get; }

    public TcpServerSocket(InternetAddress address)
    {
        var ipv6 = address.Version == 6;
        _socket = new Socket(ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            if (ipv6)
                _socket.DualMode = false;
            _socket.Bind(new IPEndPoint(IPAddress.Parse(address.Ip), address.Port));
            _socket.Listen(Backlog);
            _socket.Blocking = false;
        }
        catch (System.Net.Sockets.SocketException e)
        {
            _socket.Dispose();
            throw Utils.SocketException.FromErrno($"Could not listen on {address}", e);
        }

        BindAddress = _socket.LocalEndPoint is IPEndPoint local
            ? new InternetAddress(local.Address.ToString(), local.Port, address.Version)
            : address;
    }

    /// <summary>The next pending connection, or null when none is waiting.</summary>
    public (Socket Client, InternetAddress? Peer)? Accept()
    {
        try
        {
            if (!_socket.Poll(0, SelectMode.SelectRead))
                return null;
            var client = _socket.Accept();
            var peer = client.RemoteEndPoint is IPEndPoint remote
                ? new InternetAddress(remote.Address.ToString(), remote.Port, BindAddress.Version)
                : null;
            return (client, peer);
        }
        catch (System.Net.Sockets.SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
        {
            return null;
        }
        catch (System.Net.Sockets.SocketException e)
        {
            throw Utils.SocketException.FromErrno("Accept failed", e);
        }
    }

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _socket.Dispose();
    }
}

/// <summary>The HTTP join endpoint Bedrock clients use to reach a NetherNet server (port of <c>HttpSignaling</c>).</summary>
public sealed class HttpSignaling : ISignaling
{
    public const int MaxNetworkIdLength = 256;
    public const int DefaultMaxConnections = 256;

    private readonly InternetAddress _bindAddress;
    private readonly TlsConfiguration? _tls;
    private readonly IServerInfoProvider? _infoProvider;
    private readonly ReverseProxy? _reverseProxy;
    private readonly int _maxConnections;

    private TcpServerSocket? _socket;
    private readonly Dictionary<int, HttpConnection> _connections = new();
    private int _nextConnectionId;
    private SignalingState _state = SignalingState.Created;
    private JoinHandler? _handler;
    private IAddressBlockList? _blockList;
    private INetherNetLogger _logger = NullNetherNetLogger.Instance;
    private X509Certificate2? _certificate;

    public HttpSignaling(InternetAddress bindAddress, TlsConfiguration? tls = null, IServerInfoProvider? infoProvider = null,
        ReverseProxy? reverseProxy = null, int maxConnections = DefaultMaxConnections)
    {
        if (maxConnections < 1)
            throw new ArgumentException($"Maximum connections must be positive, got {maxConnections}");
        _bindAddress = bindAddress;
        _tls = tls;
        _infoProvider = infoProvider;
        _reverseProxy = reverseProxy;
        _maxConnections = maxConnections;
    }

    public InternetAddress BoundAddress =>
        _socket?.BindAddress ?? throw new InvalidOperationException("HTTP signaling is not listening");

    public string Describe() => "HTTP signaling on " + (_socket?.BindAddress ?? _bindAddress);

    public void Start(SignalingContext context)
    {
        _state.CheckCanStart();
        _tls?.CheckReadable();
        _certificate = _tls?.LoadCertificate();

        try
        {
            _socket = new TcpServerSocket(_bindAddress);
        }
        catch (Utils.SocketException e)
        {
            throw new SignalingException(e.Message, e);
        }

        _logger = new PrefixedNetherNetLogger(context.Logger, "HTTP signaling");
        _blockList = context.BlockList;
        _handler = new JoinHandler(context.Negotiator, context.BlockList, _infoProvider, _reverseProxy, _logger);
        _state = SignalingState.Started;
    }

    public void Tick(double now)
    {
        var socket = _socket;
        if (_state != SignalingState.Started || socket is null || _handler is null)
            return;

        AcceptAll(socket, now);

        foreach (var (id, connection) in _connections.ToArray())
        {
            connection.Advance(now, _handler);
            if (connection.IsDone)
            {
                connection.Close();
                _connections.Remove(id);
            }
        }
    }

    private void AcceptAll(TcpServerSocket socket, double now)
    {
        while (true)
        {
            (Socket Client, InternetAddress? Peer)? accepted;
            try
            {
                accepted = socket.Accept();
            }
            catch (Utils.SocketException e)
            {
                _logger.Warning(e.Message);
                return;
            }
            if (accepted is null)
                return;

            var (client, peerAddress) = accepted.Value;
            if (peerAddress is not null && _blockList!.IsBlocked(peerAddress.Ip))
            {
                _logger.Debug($"Dropped a signaling connection from blocked address {peerAddress.Ip}");
                client.Dispose();
                continue;
            }

            if (_connections.Count >= _maxConnections && !EvictStalest())
            {
                _logger.Debug($"Refused signaling connection: connection limit of {_maxConnections} reached");
                client.Dispose();
                continue;
            }

            IPolledStream stream = _certificate is not null ? new PolledTlsStream(client) : new PolledSocketStream(client);
            var connection = new HttpConnection(stream, peerAddress, _certificate, _logger, now);
            _logger.Debug($"Signaling connection opened from {connection.PeerName}");
            _connections[_nextConnectionId++] = connection;
        }
    }

    // Idle connections still waiting for a request are sacrificed first so a flood cannot starve real clients.
    private bool EvictStalest()
    {
        int? stalestId = null;
        foreach (var (id, connection) in _connections)
        {
            if (connection.State.IsAwaitingRequest() && (stalestId is null || connection.Deadline < _connections[stalestId.Value].Deadline))
                stalestId = id;
        }
        if (stalestId is null)
            return false;

        var stalest = _connections[stalestId.Value];
        _logger.Debug($"Evicting stalest signaling connection ({stalest.PeerName}) to accommodate new connection");
        stalest.Close();
        _connections.Remove(stalestId.Value);
        return true;
    }

    public void Shutdown()
    {
        if (_state == SignalingState.ShutDown)
            return;
        _state = SignalingState.ShutDown;

        foreach (var connection in _connections.Values)
            connection.Close();
        _connections.Clear();

        _socket?.Dispose();
        _socket = null;
        _certificate?.Dispose();
        _certificate = null;
    }
}
