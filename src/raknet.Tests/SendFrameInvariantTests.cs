using System.Collections.Concurrent;
using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
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

/// <summary>Expõe índices/fila só para asserts dos invariantes de saída.</summary>
file sealed class ProbeSession : RakNetSession
{
    public IReadOnlyCollection<Frame> PendingFrames => OutputFrames;
    public uint ReliableIndex => OutputReliableIndex;
    public uint OrderIndex(byte channel) => OutputOrderIndex[channel];
    public uint SequenceIndex(byte channel) => OutputSequenceIndex[channel];
}

file static class SessionFactory
{
    public static ProbeSession Create(RecordingRakNetServer server, ushort mtu = 576) =>
        new()
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19132),
            Id = 1,
            Server = server,
            MTU = mtu
        };
}

public class SendFrameInvariantTests
{
    [Fact]
    public void Split_fragments_get_distinct_MessageIndexes()
    {
        var server = new RecordingRakNetServer();
        var session = SessionFactory.Create(server, mtu: 100);
        // Payload big enough to force multiple fragments (maxSize ≈ MTU-36).
        var payload = new byte[250];
        Random.Shared.NextBytes(payload);

        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = payload
        }, RakNetSession.Priority.Immediate);

        var messageIndexes = new List<uint>();
        while (server.Captured.TryDequeue(out var datagram))
        {
            if ((datagram[0] & 0xf0) != (byte)BitFlags.Valid) continue;
            var stream = new BinaryStream(datagram[1..]);
            var frameSet = new FrameSet();
            frameSet.Decode(ref stream);
            stream.Dispose();
            foreach (var frame in frameSet.Packets)
                messageIndexes.Add(frame.MessageIndex);
        }

        Assert.True(messageIndexes.Count >= 2, $"expected split fragments, got {messageIndexes.Count}");
        Assert.Equal(messageIndexes.Count, messageIndexes.Distinct().Count());
        Assert.Equal(messageIndexes.OrderBy(x => x), messageIndexes); // strictly increasing as assigned
        Assert.All(server.Captured, datagram => Assert.True(
            datagram.Length <= 100 - RakNetSession.IP_UDP_HEADER_SIZE));
    }

    [Fact]
    public void Queued_frames_never_encode_a_datagram_larger_than_effective_udp_payload()
    {
        var server = new RecordingRakNetServer();
        const ushort mtu = 100;
        var session = SessionFactory.Create(server, mtu);

        // Each reliable ordered frame fits alone, but the pair exceeds the
        // negotiated datagram limit once FrameSet and frame headers are included.
        for (var i = 0; i < 2; i++)
        {
            session.SendFrame(new Frame
            {
                Reliability = Reliability.ReliableOrdered,
                OrderChannel = 0,
                Buffer = new byte[40]
            }, RakNetSession.Priority.Normal);
        }
        session.FlushOutgoing();

        Assert.Equal(2, server.Captured.Count);
        var maxUdpPayload = mtu - RakNetSession.IP_UDP_HEADER_SIZE;
        Assert.All(server.Captured, datagram => Assert.True(
            datagram.Length <= maxUdpPayload,
            $"encoded datagram length {datagram.Length} exceeded UDP payload limit {maxUdpPayload}"));
    }

    [Fact]
    public void Invalid_OrderChannel_does_not_throw()
    {
        var server = new RecordingRakNetServer();
        var session = SessionFactory.Create(server);

        var ex = Record.Exception(() => session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 32,
            Buffer = new byte[] { 1, 2, 3 }
        }, RakNetSession.Priority.Normal));

        Assert.Null(ex);
        Assert.Empty(session.PendingFrames);
    }

    [Fact]
    public void Ordered_frames_advance_OrderIndex_and_reset_SequenceIndex()
    {
        var server = new RecordingRakNetServer();
        var session = SessionFactory.Create(server);

        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = new byte[] { 1 }
        }, RakNetSession.Priority.Normal);

        Assert.Equal(1u, session.OrderIndex(0));
        Assert.Equal(0u, session.SequenceIndex(0));

        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = new byte[] { 2 }
        }, RakNetSession.Priority.Normal);

        Assert.Equal(2u, session.OrderIndex(0));
        var pending = session.PendingFrames.ToList();
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, f => f.OrderIndex == 0 && f.Buffer.Span[0] == 1);
        Assert.Contains(pending, f => f.OrderIndex == 1 && f.Buffer.Span[0] == 2);
    }

    [Fact]
    public void Sequenced_frames_share_OrderIndex_and_advance_SequenceIndex()
    {
        var server = new RecordingRakNetServer();
        var session = SessionFactory.Create(server);

        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableSequenced,
            OrderChannel = 1,
            Buffer = new byte[] { 1 }
        }, RakNetSession.Priority.Normal);
        session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableSequenced,
            OrderChannel = 1,
            Buffer = new byte[] { 2 }
        }, RakNetSession.Priority.Normal);

        Assert.Equal(0u, session.OrderIndex(1));
        Assert.Equal(2u, session.SequenceIndex(1));
        var pending = session.PendingFrames.Where(f => f.OrderChannel == 1).ToList();
        Assert.All(pending, f => Assert.Equal(0u, f.OrderIndex));
        Assert.Contains(pending, f => f.SequenceIndex == 0);
        Assert.Contains(pending, f => f.SequenceIndex == 1);
    }

    [Fact]
    public void Concurrent_SendFrame_does_not_corrupt_reliable_index()
    {
        var server = new RecordingRakNetServer();
        // MTU enorme evita flush espontâneo por packing; isolamos só o lock/índices.
        var session = SessionFactory.Create(server, mtu: ushort.MaxValue);
        const int threads = 8;
        const int perThread = 50;
        Parallel.For(0, threads, _ =>
        {
            for (var i = 0; i < perThread; i++)
            {
                session.SendFrame(new Frame
                {
                    Reliability = Reliability.ReliableOrdered,
                    OrderChannel = 0,
                    Buffer = new byte[] { (byte)i }
                }, RakNetSession.Priority.Normal);
            }
        });

        Assert.Equal((uint)(threads * perThread), session.ReliableIndex);
        Assert.Equal(threads * perThread, session.PendingFrames.Count);
        var indexes = session.PendingFrames.Select(f => f.MessageIndex).ToList();
        Assert.Equal(indexes.Count, indexes.Distinct().Count());
    }
}
