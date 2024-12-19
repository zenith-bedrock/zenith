using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Log;
using Zenith.Raknet.Protocol;

namespace Zenith.Raknet;

// Separate class to another file in the future for better organization
public class RakNetSession
{
    public required IPEndPoint EndPoint { get; init; }
    public int Id { get; init; }
    public long LastSeen { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Incoming(ReadOnlyMemory<byte> buffer)
    {
        LastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Console.WriteLine($"[{EndPoint}] Incoming {buffer.Length} bytes.");
    }
}

public class RakNetServer
{
    public static readonly byte[] MAGIC = { 0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe, 0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78 };

    private const int RAKNET_TPS = 20;
    private static readonly TimeSpan RAKNET_TICK = TimeSpan.FromMilliseconds(1000.0 / RAKNET_TPS);

    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly UnconnectedRakNet _unconnected;

    private readonly ConcurrentDictionary<ulong, RakNetSession> _sessions = new();


    private int _tickCount = 0;

    public ulong Guid { get; init; } = (ulong)new Random().Next(0, int.MaxValue);
    public IPEndPoint RemoteEndPoint { get; init; }
    public List<RakNetSession> Connections => _sessions.Values.ToList();
    public uint MaxConnections { get; init; } = 20;
    public ILogger? Logger { get; init; }

    public RakNetServer(int port)
    {
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

    protected int NextSessionId()
    {
        return _sessions.Values.Count > 0 ? _sessions.Values.Max(s => s.Id) + 1 : 0;
    }

    public async Task StartAsync()
    {
        Logger?.Debug("Starting RakNet connection...");

        _listener.Client.Bind(RemoteEndPoint);

        var datagramTask = ReceiveDatagramAsync(_cancellationTokenSource.Token);
        var tickTask = Task.Run(() => TickAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

        Logger?.Debug($"RakNet running at {RemoteEndPoint}");
        await Task.WhenAll(datagramTask, tickTask);
        Logger?.Debug("RakNet gracefully stopped.");
    }

    public async Task ShutdownAsync()
    {
        Logger?.Debug("Requesting RakNet shutdown...");
        _cancellationTokenSource.Cancel();
        _listener.Close();
        await Task.CompletedTask;
    }

    private async Task ReceiveDatagramAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _listener.ReceiveAsync(token);
                var buffer = result.Buffer;

                if (buffer.Length < 1)
                {
                    Logger?.Warning($"Received empty datagram from {result.RemoteEndPoint}.");
                    continue;
                }

                var remoteEndPoint = result.RemoteEndPoint.ToUInt64();

                var flags = (Datagram.BitFlags)buffer[0];
                var offline = !flags.HasFlag(Datagram.BitFlags.Valid);

                if (offline)
                {
                    _unconnected.Handle(result.RemoteEndPoint, buffer);
                    continue;
                }
                if (_sessions.TryGetValue(remoteEndPoint, out var session))
                {
                    session.Incoming(buffer);
                }
                else
                {
                    // Create new session or handle unconnected
                }
            }
            catch (OperationCanceledException)
            {
                Logger?.Debug("Packet reception stopped.");
                break;
            }
            catch (Exception ex)
            {
                Logger?.Error($"Error receiving datagram: {ex.Message}");
            }
        }
    }

    private async Task TickAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // Do stuff
            }
            catch (Exception ex)
            {
                Logger?.Error($"Error during tick: {ex.Message}");
            }

            _tickCount++;
            var elapsed = DateTime.UtcNow - startTime;

            var delay = RAKNET_TICK - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }
        }
    }

    public void Send(IPEndPoint endPoint, byte[] buffer)
    {
        Logger?.Debug($"Sending {buffer.Length} bytes...");
        _listener.Send(buffer, buffer.Length, endPoint);
    }
}
