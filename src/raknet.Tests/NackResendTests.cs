using System.Collections.Concurrent;
using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Tests;

file sealed class RecordingRakNetServer : RakNetServer
{
    public ConcurrentQueue<byte[]> Captured { get; } = new();

    public RecordingRakNetServer() : base(port: 0)
    {
    }

    public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) =>
        Captured.Enqueue(buffer.ToArray());
}

file sealed class ProbeSession : RakNetSession
{
    public uint OrderIndex(byte channel) => OutputOrderIndex[channel];
}

file static class SessionFactory
{
    public static ProbeSession Create(RecordingRakNetServer server, ushort mtu = 1400) =>
        new()
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19132),
            Id = 1,
            Server = server,
            MTU = mtu
        };

    public static List<Frame> DecodeFrames(RecordingRakNetServer server)
    {
        var frames = new List<Frame>();
        while (server.Captured.TryDequeue(out var datagram))
        {
            if ((datagram[0] & 0xf0) != (byte)BitFlags.Valid) continue;
            var stream = new BinaryStream(datagram[1..]);
            var frameSet = new FrameSet();
            frameSet.Decode(ref stream);
            stream.Dispose();
            frames.AddRange(frameSet.Packets);
        }
        return frames;
    }
}

/// <summary>
/// Regression for ADR §94: a NACK-triggered resend must retransmit the frame with its ORIGINAL
/// OrderIndex/SequenceIndex/MessageIndex, not re-derive fresh ones as if it were a new logical
/// message. Root cause of the smoke:respawn timeout — a dropped Reliable Ordered frame's resend
/// silently claimed a later OrderIndex, permanently orphaning the original slot; every subsequent
/// frame on that channel then waits forever for an order index that will never arrive again.
/// </summary>
public class NackResendTests
{
    [Fact]
    public void Nack_resend_preserves_original_order_and_message_index()
    {
        var server = new RecordingRakNetServer();
        var session = SessionFactory.Create(server);

        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = [1]
        }, RakNetSession.Priority.Immediate);

        var firstSend = SessionFactory.DecodeFrames(server);
        Assert.Single(firstSend);
        var original = firstSend[0];

        // Simulate the peer NACKing the datagram that carried this frame (sequence 0, the first
        // FrameSet sent this session).
        var nack = new NACK { Sequences = [0] };
        var encoded = nack.Encode();
        var reader = new BinaryStream(encoded[1..].ToArray());
        session.HandleNack(ref reader);
        reader.Dispose();

        var resend = SessionFactory.DecodeFrames(server);
        Assert.Single(resend);
        var resent = resend[0];

        Assert.Equal(original.OrderIndex, resent.OrderIndex);
        Assert.Equal(original.SequenceIndex, resent.SequenceIndex);
        Assert.Equal(original.MessageIndex, resent.MessageIndex);
        Assert.Equal(original.Buffer, resent.Buffer);

        // The channel's live OrderIndex counter must not have moved from a resend — only a
        // genuinely new logical frame is allowed to advance it.
        Assert.Equal(1u, session.OrderIndex(0));
    }

    [Fact]
    public void Frame_sent_after_a_resend_still_gets_the_next_order_index()
    {
        var server = new RecordingRakNetServer();
        var session = SessionFactory.Create(server);

        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = [1]
        }, RakNetSession.Priority.Immediate);
        SessionFactory.DecodeFrames(server); // drain

        var nack = new NACK { Sequences = [0] };
        var encoded = nack.Encode();
        var reader = new BinaryStream(encoded[1..].ToArray());
        session.HandleNack(ref reader);
        reader.Dispose();
        SessionFactory.DecodeFrames(server); // drain the resend

        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = [2]
        }, RakNetSession.Priority.Immediate);

        var second = SessionFactory.DecodeFrames(server);
        Assert.Single(second);
        // Must be OrderIndex 1 (the second logical frame) — not 2, which is what the pre-fix
        // bug would have produced by letting the resend consume an extra counter increment.
        Assert.Equal(1u, second[0].OrderIndex);
    }
}
