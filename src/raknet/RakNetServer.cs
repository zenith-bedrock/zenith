using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Log;

namespace Zenith.Raknet;

public class RakNetServer
{
    public static readonly byte[] MAGIC = { 0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe, 0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78 };

    private const int RAKNET_TPS = 20;
    private static readonly TimeSpan RAKNET_TICK = TimeSpan.FromMilliseconds(1000.0 / RAKNET_TPS);

    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _runTask;
    private Task? _shutdownTask;
    private readonly object _lifecycleGate = new();
    private readonly UnconnectedRakNet _unconnected;

    private readonly ConcurrentDictionary<ulong, RakNetSession> _sessions = new();

    // Rate limit generoso pra pings/handshake em geral (motd no server list, etc.) e um bem
    // mais restrito especificamente pra completar o handshake (criar sessão de verdade),
    // já que isso é o recurso mais caro de consumir.
    private readonly IpRateLimiter _unconnectedPacketLimiter = new(capacity: 30, refillPerSecond: 15);
    private readonly IpRateLimiter _connectionAttemptLimiter = new(capacity: 5, refillPerSecond: 1);

    private int _tickCount = 0;
    private int _nextSessionId = -1;
    private long _sentDatagrams;
    private long _sentBytes;
    private long _receivedDatagrams;
    private long _receivedBytes;

    public ulong Guid { get; init; }
    public IPEndPoint RemoteEndPoint { get; init; }

    /// <summary>Snapshot list — allocates. Prefer <see cref="ConnectionCount"/> for occupancy checks.</summary>
    public List<RakNetSession> Connections => _sessions.Values.ToList();

    public int ConnectionCount => _sessions.Count;
    public long SentDatagrams => Interlocked.Read(ref _sentDatagrams);
    public long SentBytes => Interlocked.Read(ref _sentBytes);
    public long ReceivedDatagrams => Interlocked.Read(ref _receivedDatagrams);
    public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);

    public uint MaxConnections { get; init; } = 20;

    /// <summary>Máximo de sessões simultâneas por IP, independente do MaxConnections global.
    /// Evita que um único host consuma todas as vagas de conexão do servidor.</summary>
    public uint MaxConnectionsPerAddress { get; init; } = 3;

    public string Motd { get; init; } = "Zenith Bedrock";
    public string SubMotd { get; init; } = "Test";
    public string ListGameMode { get; init; } = "Survival";
    public int ProtocolVersion { get; init; } = 1001;
    public string VersionName { get; init; } = "1.21.50";

    /// <summary>
    /// MOTD “online” count. When set (Zenith wires <c>PlayerManager.Count</c>), list UI
    /// shows players — not raw RakNet sessions. Null → <see cref="ConnectionCount"/>.
    /// </summary>
    public Func<int>? OnlinePlayerCount { get; set; }

    public ILogger? Logger { get; init; }
    public IRakNetSessionListener? SessionListener { get; set; } = null;

    /// <summary>Fired once UDP bind succeeds — used for panel startup detection (ADR §68).</summary>
    public Action<IPEndPoint>? OnListening { get; set; }

    private static ulong GenerateGuid()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    /// <param name="serverGuid">Stable LAN identity across restarts; null → random.</param>
    public RakNetServer(int port, ulong? serverGuid = null)
    {
        Guid = serverGuid ?? GenerateGuid();
        _listener = CreateListener();
        RemoteEndPoint = new IPEndPoint(IPAddress.Any, port);
        _unconnected = new UnconnectedRakNet(this);
    }

    private static UdpClient CreateListener()
    {
        var listener = new UdpClient
        {
            EnableBroadcast = true,
            DontFragment = false
        };

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            listener.Client.ReceiveBufferSize = int.MaxValue;
            listener.Client.SendBufferSize = int.MaxValue;
        }

        if (!OperatingSystem.IsWindows()) return listener;

        try
        {
            const uint IOC_IN = 0x80000000;
            const uint IOC_VENDOR = 0x18000000;
            const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;

            listener.Client.IOControl(
                unchecked((int)SIO_UDP_CONNRESET),
                BitConverter.GetBytes(false),
                null
            );
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Failed to apply SIO_UDP_CONNRESET: {ex.Message}");
        }
        return listener;
    }

    public int NextSessionId()
    {
        return Interlocked.Increment(ref _nextSessionId);
    }

    public bool HasSession(IPEndPoint endPoint) => _sessions.ContainsKey(endPoint.ToUInt64());

    public bool TryConsumeUnconnectedPacket(IPAddress address) => _unconnectedPacketLimiter.TryConsume(address);

    public bool TryConsumeConnectionAttempt(IPAddress address) => _connectionAttemptLimiter.TryConsume(address);

    public int CountSessionsByAddress(IPAddress address) =>
        _sessions.Values.Count(s => s.EndPoint.Address.Equals(address));

    /// <summary>
    /// Evict one session for <paramref name="address"/> to free a MaxConnectionsPerAddress
    /// slot: prefer <see cref="RakNetSession.HasGameIdentity"/> == false, then oldest LastSeen.
    /// </summary>
    public bool TryEvictSessionForAddress(IPAddress address, out RakNetSession? victim)
    {
        RakNetSession? best = null;
        foreach (var session in _sessions.Values)
        {
            if (!session.EndPoint.Address.Equals(address))
                continue;
            if (best is null)
            {
                best = session;
                continue;
            }

            var preferNew = !session.HasGameIdentity && best.HasGameIdentity;
            var bothSameIdentity = session.HasGameIdentity == best.HasGameIdentity;
            if (preferNew || (bothSameIdentity && session.LastSeen < best.LastSeen))
                best = session;
        }

        if (best is null)
        {
            victim = null;
            return false;
        }

        Logger?.Warning(
            $"Evicting session {best.EndPoint} (HasGameIdentity={best.HasGameIdentity}) " +
            $"to free a slot for {address}.");
        best.Disconnect(DisconnectReason.ServerDisconnect);
        victim = best;
        return true;
    }

    public int MotdOnlineCount => OnlinePlayerCount?.Invoke() ?? ConnectionCount;

    /// <summary>
    /// Starts the single transport lifetime. Repeated calls return the same task; a transport that has
    /// already begun shutdown is terminal and cannot bind again.
    /// </summary>
    public Task StartAsync()
    {
        lock (_lifecycleGate)
        {
            if (_runTask is not null)
                return _runTask;

            if (_shutdownTask is not null)
            {
                return Task.FromException(new InvalidOperationException(
                    "A stopped RakNetServer instance cannot be started. Create a new transport instance."));
            }

            return _runTask = RunCoreAsync();
        }
    }

    private async Task RunCoreAsync()
    {
        Logger?.Debug("Starting RakNet connection...");
        _listener.Client.Bind(RemoteEndPoint);
        OnListening?.Invoke(RemoteEndPoint);
        Logger?.Debug($"RakNet running at {RemoteEndPoint}");

        var datagramTask = ReceiveDatagramAsync(_cancellationTokenSource.Token);
        var tickTask = Task.Run(() => TickAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

        try
        {
            await Task.WhenAll(datagramTask, tickTask);
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
            // Expected coordinated shutdown.
        }
        Logger?.Debug("RakNet gracefully stopped.");
    }

    /// <summary>Stops the transport once; repeated calls return the same drain task.</summary>
    public Task ShutdownAsync()
    {
        lock (_lifecycleGate)
            return _shutdownTask ??= ShutdownCoreAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        Logger?.Debug("Requesting RakNet shutdown...");
        _cancellationTokenSource.Cancel();
        _listener.Close();
        Task? run;
        lock (_lifecycleGate)
            run = _runTask;
        if (run is not null)
        {
            try { await run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch when (_cancellationTokenSource.IsCancellationRequested)
            {
                // The owner that observed the unexpected failure preserves and propagates it.
            }
        }
    }

    private async Task ReceiveDatagramAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _listener.ReceiveAsync(token);
                var buffer = result.Buffer;
                Interlocked.Increment(ref _receivedDatagrams);
                Interlocked.Add(ref _receivedBytes, buffer.Length);

                if (buffer.Length < 1)
                {
                    Logger?.Warning($"Received empty datagram from {result.RemoteEndPoint}.");
                    continue;
                }

                var remoteEndPoint = result.RemoteEndPoint.ToUInt64();

                var flags = (BitFlags)buffer[0];
                var offline = !flags.HasFlag(BitFlags.Valid);

                if (offline)
                {
                    if (!TryConsumeUnconnectedPacket(result.RemoteEndPoint.Address))
                    {
                        Logger?.Debug($"[{result.RemoteEndPoint}] Rate limited (unconnected packet).");
                        continue;
                    }

                    _unconnected.Handle(result.RemoteEndPoint, buffer);
                    continue;
                }

                if (_sessions.TryGetValue(remoteEndPoint, out var session))
                {
                    session.Incoming(buffer);
                }
            }
            catch (OperationCanceledException)
            {
                Logger?.Debug("Packet reception stopped.");
                break;
            }
            catch (Exception ex)
            {
                Logger?.Error($"Error receiving datagram: {ex.Message}: {ex.StackTrace}");
            }
        }
    }

    public void AddSession(RakNetSession session)
    {
        _sessions.TryAdd(session.EndPoint.ToUInt64(), session);
    }

    public void RemoveSession(RakNetSession session)
    {
        _sessions.TryRemove(session.EndPoint.ToUInt64(), out _);
    }

    private async Task TickAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                foreach (var session in _sessions)
                {
                    try
                    {
                        session.Value.Tick();
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error($"Error ticking session {session.Value.EndPoint}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error($"Error during tick: {ex.Message}");
            }

            _tickCount++;

            if (_tickCount % (RAKNET_TPS * 10) == 0)
            {
                _unconnectedPacketLimiter.Cleanup(maxIdleMs: 60_000);
                _connectionAttemptLimiter.Cleanup(maxIdleMs: 60_000);
            }

            var elapsed = DateTime.UtcNow - startTime;

            var delay = RAKNET_TICK - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }
        }
    }

    public virtual void Send(IPEndPoint endPoint, byte[] buffer) =>
        Send(endPoint, (ReadOnlySpan<byte>)buffer);

    /// <summary>
    /// UDP send without an extra <c>ToArray</c>. Test doubles override this (not only the
    /// <see cref="byte"/>[] overload) so capture stays accurate.
    /// </summary>
    public virtual void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer)
    {
        _listener.Client.SendTo(buffer, SocketFlags.None, endPoint);
        Interlocked.Increment(ref _sentDatagrams);
        Interlocked.Add(ref _sentBytes, buffer.Length);
    }
}
