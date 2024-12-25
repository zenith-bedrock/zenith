using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Log;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

// Separate class to another file in the future for better organization
public class RakNetSession
{
    public enum Priority
    {
        Normal,
        Immediate
    }

    public const int MTU = 1492; // TODO: mtu should not be a const
    public const int DGRAM_HEADER_SIZE = 4;
    public const int DGRAM_MTU_OVERHEAD = 36;

    public required IPEndPoint EndPoint { get; init; }
    public int Id { get; init; }
    public required RakNetServer Server { get; init; }
    public long LastSeen { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    protected readonly HashSet<uint> ReceivedFrameSequences = new();
    protected readonly HashSet<uint> LostFrameSequences = new();
    protected readonly uint[] InputHighestSequenceIndex = new uint[32];
    // protected readonly HashSet<uint> fragmentsQueue: Map<number, Map<number, Frame>> = new Map();

    protected readonly int[] InputOrderIndex = new int[32];
    protected Dictionary<byte, Dictionary<uint, Frame>> InputOrderingQueue = new();
    protected int LastInputSequence = -1;

    protected readonly uint[] OutputOrderIndex = new uint[32];
    protected readonly uint[] OutputSequenceIndex = new uint[32];

    protected HashSet<Frame> OutputFrames = new();
    protected Dictionary<uint, List<Frame>> OutputBackup = new();

    protected uint OutputSequence = 0;

    // protected outputsplitIndex = 0;

    // protected outputReliableIndex = 0;

    public RakNetSession()
    {
        for (byte index = 0; index < 32; index++)
        {
            InputOrderingQueue[index] = new();
        }
    }

    public void Tick()
    {
        // TODO: disconnect

        if (ReceivedFrameSequences.Count > 0)
        {
            var ack = new ACK
            {
                Sequences = ReceivedFrameSequences.ToArray()
            };
            ReceivedFrameSequences.Clear();
            Server.Send(EndPoint, ack.Encode());
        }

        if (LostFrameSequences.Count > 0)
        {
            var nack = new NACK
            {
                Sequences = LostFrameSequences.ToArray()
            };
            LostFrameSequences.Clear();
            Server.Send(EndPoint, nack.Encode());
        }

        SendQueue(OutputFrames.Count);
    }

    void SendQueue(int count)
    {
        if (OutputFrames.Count == 0) return;

        Server.Logger?.Debug($"[{EndPoint}] Queue {count}");

        var frameSet = new FrameSet
        {
            Sequence = OutputSequence++,
            Packets = OutputFrames.Take(count).ToList()
        };

        OutputBackup[frameSet.Sequence] = frameSet.Packets;

        foreach (var frame in frameSet.Packets) OutputFrames.Remove(frame);

        Server.Send(EndPoint, frameSet.Encode());
    }

    public void SendFrame(Frame frame, Priority priority)
    {
        if (Frame.IsSequenced(frame.Reliability))
        {
            frame.OrderIndex = OutputOrderIndex[frame.OrderChannel];
            frame.SequenceIndex = OutputSequenceIndex[frame.OrderChannel]++;
        }
        else if (Frame.IsOrdered(frame.Reliability))
        {
            frame.OrderIndex = OutputOrderIndex[frame.OrderChannel]++;
            OutputSequenceIndex[frame.OrderChannel] = 0;
        }

        // TODO: split packet

        QueueFrame(frame, priority);
    }

    private void QueueFrame(Frame frame, Priority priority)
    {
        var length = DGRAM_HEADER_SIZE;

        foreach (var outputFrame in OutputFrames) length += frame.GetByteLength();

        if (length + frame.GetByteLength() > MTU + DGRAM_MTU_OVERHEAD) SendQueue(OutputFrames.Count);

        OutputFrames.Add(frame);
        Server.Logger?.Debug($"[{EndPoint} Added to output frame");
        if (priority == Priority.Immediate) SendQueue(1);
    }

    public void Incoming(byte[] buffer)
    {
        LastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Server.Logger?.Debug($"[{EndPoint} Incoming {buffer.Length} bytes.");

        var flag = buffer[0] & 0xf0;
        var reader = new BinaryStream(buffer[1..]);
        switch (flag)
        {
            default:
                Server.Logger?.Debug($"[{EndPoint}] Unknown flag: {flag:X2}");
                break;
            case (byte)MessageIdentifier.Ack:
                Server.Logger?.Debug($"[{EndPoint}] Received ACK.");
                HandleAck(reader);
                break;
            case (byte)MessageIdentifier.Nack:
                Server.Logger?.Debug($"[{EndPoint}] Received NACK.");
                HandleNack(reader);
                break;
            case (byte)BitFlags.Valid:
                HandleIncomingFrameSet(reader);
                break;
        }
        reader.Dispose();
    }

    public void HandleAck(BinaryStream reader)
    {
        var ack = IPacket.From<ACK>(reader);
        foreach (var sequence in ack.Sequences)
        {
            OutputBackup.Remove(sequence);
        }
    }

    public void HandleNack(BinaryStream reader)
    {
        var nack = IPacket.From<NACK>(reader);
        foreach (var sequence in nack.Sequences)
        {
            if (OutputBackup.TryGetValue(sequence, out var frames))
            {
                foreach (var frame in frames)
                {
                    SendFrame(frame, Priority.Immediate);
                }
            }
        }
    }

    private void HandleIncomingFrameSet(BinaryStream reader)
    {
        var frameSet = new FrameSet();
        frameSet.Decode(reader);

        if (ReceivedFrameSequences.Contains(frameSet.Sequence)) return; // TODO: duplicate framesets

        LostFrameSequences.Remove(frameSet.Sequence);

        if (frameSet.Sequence < LastInputSequence || frameSet.Sequence == LastInputSequence) return; // TODO: out of order

        ReceivedFrameSequences.Add(frameSet.Sequence);

        if (frameSet.Sequence - LastInputSequence > 1)
        {
            for (
                var index = (uint)(LastInputSequence + 1);
                index < frameSet.Sequence;
                index++
            ) LostFrameSequences.Add(index);
        }

        LastInputSequence = (int)frameSet.Sequence;

        foreach (var packet in frameSet.Packets)
        {
            HandleFrame(packet);
        }
    }

    private bool HandleFragment(Frame frame)
    {
        return false; // TODO: handle split frame
    }

    private bool HandleIncomingBatch(byte[] buffer)
    {
        var pid = buffer[0];
        var reader = new BinaryStream(buffer[1..]);

        Server.Logger?.Debug($"Connected PID: {pid}");

        switch (pid)
        {
            case (byte)MessageIdentifier.ConnectedPing:
                var connectedPing = IPacket.From<ConnectedPing>(reader);
                Server.Logger?.Debug($"ConnectedPing: {connectedPing.SendPingTime}");

                var connectedPong = new ConnectedPong
                {
                    SendPingTime = connectedPing.SendPingTime,
                    SendPongTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                SendFrame(new Frame
                {
                    Reliability = Reliability.ReliableOrdered,
                    OrderChannel = 0,
                    Buffer = connectedPong.Encode().ToArray()
                }, Priority.Normal);
                return true;
            case (byte)MessageIdentifier.ConnectionRequest:
                var connectionRequest = IPacket.From<ConnectionRequest>(reader);
                Server.Logger?.Debug($"ConnectionRequest: {connectionRequest.ClientGuid} {connectionRequest.SendPingTime} {connectionRequest.UseSecurity}");

                var connectionRequestAccepted = new ConnectionRequestAccepted
                {
                    Address = EndPoint,
                    SystemIndex = 0,
                    SendPingTime = connectionRequest.SendPingTime,
                    SendPongTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                SendFrame(new Frame
                {
                    Reliability = Reliability.ReliableOrdered,
                    OrderChannel = 0,
                    Buffer = connectionRequestAccepted.Encode().ToArray()
                }, Priority.Normal);
                return true;
        }

        Server.Logger?.Debug($"[{EndPoint}] Unhandled {pid}");
        return false;
    }

    private bool HandleFrame(Frame frame)
    {
        if (frame.IsSplit()) return HandleFragment(frame);

        if (Frame.IsSequenced(frame.Reliability))
        {
            if (frame.SequenceIndex < InputHighestSequenceIndex[frame.OrderChannel] || frame.OrderIndex < InputOrderIndex[frame.OrderChannel])
            {
                return false;
            }

            InputHighestSequenceIndex[frame.OrderChannel] = frame.SequenceIndex + 1;
            return HandleIncomingBatch(frame.Buffer);
        }
        else if (Frame.IsOrdered(frame.Reliability))
        {
            if (frame.OrderIndex == InputOrderIndex[frame.OrderChannel]!)
            {
                InputHighestSequenceIndex[frame.OrderChannel] = 0;
                InputOrderIndex[frame.OrderChannel] = frame.OrderChannel + 1;

                HandleIncomingBatch(frame.Buffer);
                var index = InputOrderIndex[frame.OrderChannel];

                var outOfOrderQueue = InputOrderingQueue[frame.OrderChannel];

                for (; outOfOrderQueue.ContainsKey((uint)index); index++)
                {
                    if (outOfOrderQueue.TryGetValue((uint)index, out var frameQueue))
                    {
                        HandleIncomingBatch(frameQueue.Buffer);
                        outOfOrderQueue.Remove((uint)index);
                    }
                    else break;
                }

                InputOrderingQueue[frame.OrderChannel] = outOfOrderQueue;
                InputOrderIndex[frame.OrderChannel] = index;
                return true;
            }
            else if (frame.OrderIndex > InputOrderIndex[frame.OrderChannel])
            {
                if (InputOrderingQueue.TryGetValue(frame.OrderChannel, out var unordered))
                {
                    HandleIncomingBatch(frame.Buffer);
                    unordered[frame.OrderIndex] = frame;
                }
                return true;
            }
        }
        else
        {
            return HandleIncomingBatch(frame.Buffer);
        }

        return false;
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
        Logger?.Debug($"RakNet running at {RemoteEndPoint}");

        var datagramTask = ReceiveDatagramAsync(_cancellationTokenSource.Token);
        var tickTask = Task.Run(() => TickAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

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

                var flags = (BitFlags)buffer[0];
                var offline = !flags.HasFlag(BitFlags.Valid);

                if (offline)
                {
                    _unconnected.Handle(result.RemoteEndPoint, buffer);
                    continue;
                }

                if (!_sessions.ContainsKey(remoteEndPoint))
                {
                    _sessions.TryAdd(remoteEndPoint, new RakNetSession
                    {
                        EndPoint = result.RemoteEndPoint,
                        Id = NextSessionId(),
                        Server = this
                    });
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

    private async Task TickAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                foreach (var session in _sessions)
                {
                    session.Value.Tick();
                }
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
        _listener.Send(buffer, buffer.Length, endPoint);
    }

    public void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) => Send(endPoint, buffer.ToArray());
}
