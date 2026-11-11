using System.Collections.Concurrent;
using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Server;
using Zenith.NetherNet.Axolotl.Signaling;
using Zenith.NetherNet.Axolotl.Signaling.Http;
using Zenith.NetherNet.Axolotl.Signaling.Lan;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Transport.WebRtc;
using Zenith.NetherNet.Axolotl.Utils;
using Zenith.Binary;

namespace Zenith.Network.NetherNet;

/// <summary>Everything the NetherNet transport needs, already resolved from configuration.</summary>
internal sealed record NetherNetTransportSettings(
    InternetAddress SignalingAddress,
    TlsConfiguration? Tls,
    ReverseProxy? ReverseProxy,
    InternetAddress? LanAddress,
    ServerIdentity Identity,
    string IdentityDomain,
    FranchiseTokenVerifier? FranchiseVerifier,
    bool AllowAnonymous,
    WebRtcTransportFactory WebRtc,
    ConnectionBudgetConfiguration Budget,
    IReadOnlyList<string>? AdvertisedAddresses,
    Func<ServerInfo> Advertisement);

/// <summary>One NetherNet peer seen as a Bedrock game transport connection.</summary>
internal sealed class NetherNetGameConnection(
    NetherNetServerRunner server, int sessionId, string networkId, TransportIdentity? identity) : IGameTransportConnection
{
    private int _hasGameIdentity;
    private int _closed;

    public int SessionId { get; } = sessionId;
    public string EndPoint => $"NetherNet/{networkId}";
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public bool HasGameIdentity
    {
        get => Volatile.Read(ref _hasGameIdentity) != 0;
        set => Volatile.Write(ref _hasGameIdentity, value ? 1 : 0);
    }
    public TransportIdentity? TransportIdentity { get; } = identity;
    public bool RequiresTransportIdentityBinding => true;
    public bool IncludesGamePacketIdentifier => false;

    public void SendGameBatch(ReadOnlyMemory<byte> batch, GameTransportPriority priority, byte orderChannel) =>
        Send(batch, Reliability.Reliable);

    public void SendUnreliableGameBatch(ReadOnlyMemory<byte> batch) => Send(batch, Reliability.Unreliable);

    /// <summary>The server thread flushes on every tick, so there is nothing to push from here.</summary>
    public void FlushOutgoing() { }

    public void Disconnect()
    {
        if (MarkClosed())
            server.CloseSession(SessionId);
    }

    /// <summary>Records that the session ended; true the first time only.</summary>
    public bool MarkClosed() => Interlocked.Exchange(ref _closed, 1) == 0;

    private void Send(ReadOnlyMemory<byte> batch, Reliability reliability)
    {
        if (!IsClosed)
            server.SendPacket(SessionId, batch.ToArray(), reliability);
    }
}

/// <summary>
/// Hosts a <see cref="NetherNetServer"/> on its own thread and translates its session callbacks to
/// <see cref="IGameTransportHost"/> calls.
/// </summary>
internal sealed class NetherNetTransport : IGameTransport, IServerEventListener
{
    private readonly IGameTransportHost _host;
    private readonly NetherNetTransportSettings _settings;
    private readonly INetherNetLogger _logger;
    private readonly ConcurrentDictionary<int, NetherNetGameConnection> _connections = new();
    private readonly NetherNetServerRunner _runner;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _keyRefresh;
    private Task? _shutdown;
    private long _sentBytes;
    private long _receivedBytes;
    private long _receivedMessages;

    public NetherNetTransport(IGameTransportHost host, NetherNetTransportSettings settings, INetherNetLogger logger)
    {
        _host = host;
        _settings = settings;
        _logger = logger;

        var configuration = new ServerConfiguration(
            new SelfSignedIdentityProvider(settings.Identity, settings.IdentityDomain),
            logger,
            new AssertionIdentityVerifier(settings.AllowAnonymous, settings.FranchiseVerifier),
            settings.WebRtc,
            settings.Budget,
            advertisedAddresses: settings.AdvertisedAddresses);
        var server = NetherNetServer.Create(configuration, this);
        var info = new DelegateServerInfoProvider(settings.Advertisement);
        server.AddSignaling(new HttpSignaling(settings.SignalingAddress, settings.Tls, info, settings.ReverseProxy));
        if (settings.LanAddress is not null)
            server.AddSignaling(new LanSignaling(info, NewNetworkId(), settings.LanAddress), optional: true);
        _runner = new NetherNetServerRunner(server, logger);
    }

    public string Name => "NetherNet";

    public TransportStatistics Statistics => new(
        _connections.Count,
        0, Interlocked.Read(ref _sentBytes),
        Interlocked.Read(ref _receivedMessages), Interlocked.Read(ref _receivedBytes));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_settings.FranchiseVerifier is { } verifier)
        {
            // Block the first connection on a key set so early FULL logins are not rejected for lack of keys.
            _keyRefresh = verifier.KeepKeysFreshAsync(message => _logger.Warning(message), _lifetime.Token);
            await Task.WhenAny(_keyRefresh, WaitForKeysAsync(verifier, cancellationToken)).ConfigureAwait(false);
        }

        _runner.Start();
        using var registration = cancellationToken.Register(() => _ = ShutdownAsync());
        await _runner.Stopped.ConfigureAwait(false);
    }

    public void StopAccepting() => _runner.StopAccepting();

    public Task ShutdownAsync() => _shutdown ??= Task.Run(() =>
    {
        _lifetime.Cancel();
        _runner.Stop();
        _runner.Dispose();
    });

    public void OnSessionOpen(int sessionId, string networkId, InternetAddress address, string? clientAddress, PeerIdentity? identity)
    {
        TransportIdentity? proven = identity is null
            ? null
            : new TransportIdentity(identity.Token.ToString(),
                Authenticated: _settings.FranchiseVerifier?.Authenticates(identity.Token) ?? false);
        var connection = new NetherNetGameConnection(_runner, sessionId, networkId, proven);
        _connections[sessionId] = connection;
        if (!_host.TryOpen(connection))
            connection.Disconnect();
    }

    public void OnSessionClose(int sessionId, DisconnectReason reason)
    {
        if (!_connections.TryRemove(sessionId, out var connection))
            return;
        connection.MarkClosed();
        _host.Close(connection, reason switch
        {
            DisconnectReason.PeerDisconnect => GameTransportCloseReason.ClientDisconnect,
            DisconnectReason.ServerDisconnect or DisconnectReason.ServerShutdown or DisconnectReason.RejectedByHost
                or DisconnectReason.AddressBlocked => GameTransportCloseReason.ServerDisconnect,
            _ => GameTransportCloseReason.Timeout
        });
    }

    public bool CanAcceptPackets() => true;

    public void OnPacketReceive(int sessionId, byte[] payload, Reliability reliability)
    {
        if (!_connections.TryGetValue(sessionId, out var connection))
            return;
        Interlocked.Increment(ref _receivedMessages);
        var batch = new BinaryStream(payload);
        _host.Receive(connection, ref batch);
    }

    public void OnPacketAck(int sessionId, int receiptId) { }

    public void OnBandwidthStatsUpdate(long bytesSentDiff, long bytesReceivedDiff)
    {
        Interlocked.Add(ref _sentBytes, bytesSentDiff);
        Interlocked.Add(ref _receivedBytes, bytesReceivedDiff);
    }

    public void OnPingMeasure(int sessionId, int pingMs) { }

    private static async Task WaitForKeysAsync(FranchiseTokenVerifier verifier, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!verifier.HasKeys && DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
    }

    private static ulong NewNetworkId()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes) | 1UL;
    }

    private sealed class DelegateServerInfoProvider(Func<ServerInfo> advertisement) : IServerInfoProvider
    {
        public ServerInfo? GetServerInfo() => advertisement();
    }
}
