using System.Net;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet;

public class RakNetSession
{
    public enum Priority
    {
        Normal,
        Immediate
    }

    public const int DGRAM_HEADER_SIZE = 4;
    public const int DGRAM_MTU_OVERHEAD = 36;

    public required IPEndPoint EndPoint { get; init; }
    public int Id { get; init; }
    public required RakNetServer Server { get; init; }
    public required ushort MTU { get; init; }
    public long LastSeen { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    protected readonly HashSet<uint> ReceivedFrameSequences = new();
    protected readonly HashSet<uint> LostFrameSequences = new();
    protected readonly uint[] InputHighestSequenceIndex = new uint[32];
    protected readonly Dictionary<short, Dictionary<int, Frame>> FragmentsQueue = new();

    protected readonly int[] InputOrderIndex = new int[32];
    protected readonly Dictionary<byte, Dictionary<uint, Frame>> InputOrderingQueue = new();
    protected int LastInputSequence = -1;

    protected readonly uint[] OutputOrderIndex = new uint[32];
    protected readonly uint[] OutputSequenceIndex = new uint[32];

    protected readonly HashSet<Frame> OutputFrames = new();
    protected readonly Dictionary<uint, List<Frame>> OutputBackup = new();

    protected uint OutputSequence;
    protected int OutputSplitIndex;
    protected uint OutputReliableIndex;

    public RakNetSession()
    {
        for (byte index = 0; index < 32; index++)
        {
            InputOrderingQueue[index] = new();
        }
    }

    public void Disconnect()
    {
        var disconnect = new Disconnect();

        var frame = new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = disconnect.Encode().ToArray()
        };

        SendFrame(frame, Priority.Immediate);

        Server.RemoveSession(this);
    }

    public void Tick()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (LastSeen + 15000 < now)
        {
            Disconnect();
            return;
        }

        if (ReceivedFrameSequences.Count > 0)
        {
            var ack = new ACK
            {
                Sequences = ReceivedFrameSequences.ToList()
            };
            ReceivedFrameSequences.Clear();
            Server.Send(EndPoint, ack.Encode());
        }

        if (LostFrameSequences.Count > 0)
        {
            var nack = new NACK
            {
                Sequences = LostFrameSequences.ToList()
            };
            LostFrameSequences.Clear();
            Server.Send(EndPoint, nack.Encode());
        }

        SendQueue(OutputFrames.Count);
    }

    private void SendQueue(int count)
    {
        if (OutputFrames.Count == 0) return;

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

        var maxSize = MTU - 36;
        var splitSize = (int)Math.Ceiling((double)frame.Buffer.Length / maxSize);

        frame.MessageIndex = OutputReliableIndex++;

        if (frame.Buffer.Length > maxSize)
        {
            var splitId = (short)(OutputSplitIndex++ % 65_536);
            for (var i = 0; i < frame.Buffer.Length; i += maxSize)
            {
                var newFrame = new Frame
                {
                    Reliability = frame.Reliability,
                    SequenceIndex = frame.SequenceIndex,
                    OrderIndex = frame.OrderIndex,
                    OrderChannel = frame.OrderChannel,
                    SplitInfo = new Frame.SplitPacketInfo(splitSize, splitId, i / maxSize),
                    Buffer = frame.Buffer.Skip(i).Take(maxSize).ToArray()
                };

                QueueFrame(newFrame, priority);
            }
        }
        else
        {
            QueueFrame(frame, priority);
        }
    }

    private void QueueFrame(Frame frame, Priority priority)
    {
        var length = DGRAM_HEADER_SIZE + OutputFrames.Sum(outputFrame => outputFrame.GetByteLength());

        if (length + frame.GetByteLength() > MTU + DGRAM_MTU_OVERHEAD) SendQueue(OutputFrames.Count);

        OutputFrames.Add(frame);
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
            if (!OutputBackup.TryGetValue(sequence, out var frames)) continue;
            foreach (var frame in frames)
            {
                SendFrame(frame, Priority.Immediate);
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
        if (!frame.IsSplit()) return false;

        if (FragmentsQueue.TryGetValue(frame.SplitInfo!.Id, out var fragment))
        {
            fragment[frame.SplitInfo.Index] = frame;

            if (fragment.Count != frame.SplitInfo.Count) return false;
            var stream = new BinaryStream();
            foreach (var frag in fragment) stream.Write(frag.Value.Buffer);

            var newFrame = new Frame
            {
                Reliability = frame.Reliability,
                MessageIndex = frame.MessageIndex,
                SequenceIndex = frame.SequenceIndex,
                OrderIndex = frame.OrderIndex,
                OrderChannel = frame.OrderChannel,
                Buffer = stream.GetBufferDisposing().ToArray()
            };

            FragmentsQueue.Remove(frame.SplitInfo.Id);
            return HandleFrame(newFrame);
        }
        FragmentsQueue[frame.SplitInfo.Id] = new()
        {
            [frame.SplitInfo.Index] = frame
        };
        return false;
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
            case (byte)MessageIdentifier.Disconnect:
                Server.RemoveSession(this);
                reader.Dispose();
                return true;
            case (byte)MessageIdentifier.Game:
                return Server.SessionListener?.HandleGamePacket(this, reader) ?? false;
        }

        Server.Logger?.Debug($"[{EndPoint}] Unhandled {pid}");
        reader.Dispose();
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

        if (!Frame.IsOrdered(frame.Reliability)) return HandleIncomingBatch(frame.Buffer);
        
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

        if (frame.OrderIndex <= InputOrderIndex[frame.OrderChannel]) return false;
        if (!InputOrderingQueue.TryGetValue(frame.OrderChannel, out var unordered)) return true;
        HandleIncomingBatch(frame.Buffer);
        unordered[frame.OrderIndex] = frame;
        return true;

    }
}