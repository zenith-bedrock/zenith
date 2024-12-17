using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Zenith.Raknet.Protocol;

namespace Zenith.Raknet;

// Separate class to another file in the future for better organization
public class RakNetSession
{
    public required string Address { get; init; }
    public int Id { get; init; }
    public long LastSeen { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Incoming(ReadOnlyMemory<byte> buffer)
    {
        LastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Console.WriteLine($"[{Address}] Incoming {buffer.Length} bytes.");
    }
}

public class RakNetServer
{
    private const int RAKNET_TPS = 20;
    private static readonly TimeSpan RAKNET_TICK = TimeSpan.FromMilliseconds(1000.0 / RAKNET_TPS);

    private readonly UdpClient _listener;
    private readonly IPEndPoint _remoteEndPoint;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private int _tickCount = 0;
    
    private readonly ConcurrentDictionary<string, RakNetSession> _sessions = new();

    public RakNetServer(int port)
    {
        _listener = CreateListener();
        _remoteEndPoint = new IPEndPoint(IPAddress.Any, port);
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
                unchecked((int) SIO_UDP_CONNRESET),
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
        Console.WriteLine("Starting RakNet connection...");

        _listener.Client.Bind(_remoteEndPoint);

        var datagramTask = ReceiveDatagramAsync(_cancellationTokenSource.Token);
        var tickTask = Task.Run(() => TickAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

        Console.WriteLine($"RakNet running at {_remoteEndPoint}");
        await Task.WhenAll(datagramTask, tickTask);
        Console.WriteLine("RakNet connection stopped.");
    }

    public async Task ShutdownAsync()
    {
        Console.WriteLine("Shutting down RakNet connection...");
        _cancellationTokenSource.Cancel();
        _listener.Close();
        Console.WriteLine("RakNet connection shut down.");
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
                    Console.WriteLine("Received empty datagram.");
                    continue;
                }

                var flags = (Datagram.BitFlags) buffer[0];
                var offline = !flags.HasFlag(Datagram.BitFlags.Valid);
                if (offline) {
                    Console.WriteLine("Received offline datagram.");
                    continue;
                }

                var remoteEndPoint = result.RemoteEndPoint.ToString();
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
                Console.WriteLine("Packet reception stopped.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving datagram: {ex.Message}");
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
                Console.WriteLine($"Error during tick: {ex.Message}");
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
}
