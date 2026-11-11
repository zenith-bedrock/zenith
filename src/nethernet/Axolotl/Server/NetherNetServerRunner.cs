using System.Collections.Concurrent;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Server;

/// <summary>
/// Drives a <see cref="NetherNetServer"/> from a dedicated thread. The server itself is single-threaded and tick-driven;
/// the runner owns that thread and turns calls from other threads into commands executed between ticks, so embedding
/// code can call <see cref="SendPacket"/> and friends from anywhere.
/// </summary>
public sealed class NetherNetServerRunner : IDisposable
{
    private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    private readonly NetherNetServer _server;
    private readonly INetherNetLogger _logger;
    private readonly TimeSpan _tickInterval;
    private readonly ConcurrentQueue<Action<NetherNetServer>> _commands = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private Thread? _thread;
    private volatile bool _stopRequested;

    public NetherNetServerRunner(NetherNetServer server, INetherNetLogger logger, TimeSpan? tickInterval = null)
    {
        _server = server;
        _logger = logger;
        _tickInterval = tickInterval ?? DefaultTickInterval;
    }

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Starts the server (signaling failures throw here, on the caller) and then its tick thread.</summary>
    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("The runner is already started");

        _server.Start();
        _thread = new Thread(Run) { Name = "NetherNet", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Queues a command to run on the server thread; commands run in order before each tick.</summary>
    public void Post(Action<NetherNetServer> command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    public void SendPacket(int sessionId, byte[] payload, Reliability reliability = Reliability.Reliable, int? receiptId = null) =>
        Post(server => server.SendPacket(sessionId, payload, reliability, receiptId));

    public void CloseSession(int sessionId, DisconnectReason reason = DisconnectReason.ServerDisconnect) =>
        Post(server => server.CloseSession(sessionId, reason));

    public void BlockAddress(string address, int timeout = 300) =>
        Post(server => server.BlockAddress(address, timeout));

    public void UnblockAddress(string address) => Post(server => server.UnblockAddress(address));

    /// <summary>Stops accepting new connections; existing sessions keep running until <see cref="Stop"/>.</summary>
    public void StopAccepting(DisconnectReason reason = DisconnectReason.ServerShutdown) =>
        Post(server => server.InitiateShutdown(reason));

    /// <summary>Disconnects every session and waits (bounded) for the server thread to finish.</summary>
    public void Stop()
    {
        if (_thread is null)
            return;
        Post(server =>
        {
            if (server.State == ServerState.Running)
                server.InitiateShutdown();
        });
        _stopRequested = true;
        _wake.Set();
        if (!_thread.Join(ShutdownGrace + TimeSpan.FromSeconds(1)))
            _logger.Warning("NetherNet server thread did not stop in time");
    }

    private void Run()
    {
        var deadline = (DateTime?)null;
        while (true)
        {
            while (_commands.TryDequeue(out var command))
                Guard(() => command(_server));

            Guard(() => _server.Tick());

            if (_server.State == ServerState.Stopped)
                break;
            if (_stopRequested)
            {
                deadline ??= DateTime.UtcNow + ShutdownGrace;
                if (DateTime.UtcNow >= deadline)
                {
                    _server.Shutdown();
                    break;
                }
            }

            _wake.Wait(_tickInterval);
            _wake.Reset();
        }
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _logger.Error($"NetherNet server error: {e}");
        }
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }
}
