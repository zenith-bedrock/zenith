using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Tests;

public class ReliableReceiveTests
{
    private sealed class CountingListener : IRakNetSessionListener
    {
        public int GamePackets { get; private set; }

        public bool HandleGamePacket(RakNetSession session, ref BinaryStream stream)
        {
            GamePackets++;
            stream.Dispose();
            return true;
        }
    }

    private sealed class RecordingServer : RakNetServer
    {
        public List<byte[]> Captured { get; } = [];

        public RecordingServer() : base(port: 0) { }

        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) =>
            Captured.Add(buffer.ToArray());
    }

    private sealed class ProbeSession : RakNetSession
    {
        public int FragmentQueueCount => FragmentsQueue.Count;

        public void SeedBackup(uint sequence, List<Frame> frames) =>
            UnacknowledgedFrameSets[sequence] = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), frames);
    }

    private static Frame GameFrame(uint messageIndex, uint orderIndex = 0) => new()
    {
        Reliability = Reliability.ReliableOrdered,
        MessageIndex = messageIndex,
        OrderIndex = orderIndex,
        OrderChannel = 0,
        Buffer = new byte[] { (byte)MessageIdentifier.Game, 0x01, 0x02 }
    };

    [Fact]
    public void Reliable_duplicate_MessageIndex_is_rejected()
    {
        var listener = new CountingListener();
        var server = new RakNetServer(0) { SessionListener = listener };
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19134),
            Id = 1,
            Server = server,
            MTU = 576
        };

        Assert.True(session.HandleFrameForTests(GameFrame(5, orderIndex: 0)));
        Assert.Equal(1, listener.GamePackets);

        Assert.False(session.HandleFrameForTests(GameFrame(5, orderIndex: 1)));
        Assert.Equal(1, listener.GamePackets);
    }

    [Fact]
    public void Fragment_count_over_512_is_dropped()
    {
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19134),
            Id = 1,
            Server = new RakNetServer(0),
            MTU = 576
        };

        var ok = session.HandleFrameForTests(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            MessageIndex = 1,
            OrderIndex = 0,
            OrderChannel = 0,
            SplitInfo = new Frame.SplitPacketInfo(Count: 513, Id: 1, Index: 0),
            Buffer = new byte[] { 0xFE }
        });

        Assert.False(ok);
        Assert.Equal(0, session.FragmentQueueCount);
    }

    [Fact]
    public void Concurrent_fragmented_messages_capped_at_32()
    {
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19134),
            Id = 1,
            Server = new RakNetServer(0),
            MTU = 576
        };
        uint messageIndex = 1;

        for (short splitId = 0; splitId < 32; splitId++)
        {
            Assert.False(session.HandleFrameForTests(new Frame
            {
                Reliability = Reliability.ReliableOrdered,
                MessageIndex = messageIndex++,
                OrderIndex = 0,
                OrderChannel = 0,
                SplitInfo = new Frame.SplitPacketInfo(Count: 2, Id: splitId, Index: 0),
                Buffer = new byte[] { 0xFE }
            }));
        }

        Assert.Equal(32, session.FragmentQueueCount);

        Assert.False(session.HandleFrameForTests(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            MessageIndex = messageIndex,
            OrderIndex = 0,
            OrderChannel = 0,
            SplitInfo = new Frame.SplitPacketInfo(Count: 2, Id: 99, Index: 0),
            Buffer = new byte[] { 0xFE }
        }));
        Assert.Equal(32, session.FragmentQueueCount);
    }

    [Fact]
    public void Nack_retransmits_backup_via_SendFrameLocked()
    {
        var server = new RecordingServer();
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19134),
            Id = 1,
            Server = server,
            MTU = 576
        };

        var backupFrame = new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = new byte[] { (byte)MessageIdentifier.ConnectedPing, 0, 0, 0, 0, 0, 0, 0, 0 }
        };
        session.SeedBackup(7, [backupFrame]);

        var nackBytes = new NACK { Sequences = [7] }.Encode().ToArray();
        var stream = new BinaryStream(nackBytes[1..]);
        session.HandleNack(ref stream);

        Assert.NotEmpty(server.Captured);
        Assert.Contains(server.Captured, d => (d[0] & 0xf0) == (byte)BitFlags.Valid);
    }
}
