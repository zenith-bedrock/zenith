using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Tests;

/// <summary>
/// Cross-reference audit finding, Phase XXIII-B polish pass: retransmission was previously
/// NACK-only. If the single NACK datagram reporting a loss was itself dropped by UDP — exactly as
/// likely as any other datagram being dropped — the sender never learned and the backed-up FrameSet
/// sat forever, permanently stalling that order channel. <see cref="RakNetSession.Tick"/> now also
/// resends any backup entry that's gone unacknowledged (no ACK, no NACK) past a fixed timeout.
/// </summary>
public class ResendTimeoutTests
{
    private sealed class RecordingServer : RakNetServer
    {
        public List<byte[]> Captured { get; } = [];
        public RecordingServer() : base(port: 0) { }
        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) => Captured.Add(buffer.ToArray());
    }

    private sealed class ProbeSession : RakNetSession
    {
        public void SeedBackup(uint sequence, long sentAtMs, List<Frame> frames) =>
            UnacknowledgedFrameSets[sequence] = (sentAtMs, frames);

        public int UnacknowledgedCount => UnacknowledgedFrameSets.Count;
    }

    private static List<Frame> DecodeFrames(RecordingServer server)
    {
        var frames = new List<Frame>();
        foreach (var datagram in server.Captured)
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

    [Fact]
    public void A_backup_entry_older_than_the_resend_timeout_is_retransmitted_without_a_nack()
    {
        var server = new RecordingServer();
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19134),
            Id = 1,
            Server = server,
            MTU = 1400
        };

        var frame = new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            MessageIndex = 5,
            OrderIndex = 0,
            OrderChannel = 0,
            Buffer = [1, 2, 3]
        };
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        session.SeedBackup(0, now - 2000, [frame]); // older than the 1500ms resend timeout

        session.Tick();

        var resent = DecodeFrames(server);
        Assert.Contains(resent, f => f.MessageIndex == 5 && f.Buffer.SequenceEqual(frame.Buffer));
    }

    [Fact]
    public void A_fresh_backup_entry_is_not_retransmitted_before_the_timeout()
    {
        var server = new RecordingServer();
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19135),
            Id = 1,
            Server = server,
            MTU = 1400
        };

        var frame = new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            MessageIndex = 9,
            OrderIndex = 0,
            OrderChannel = 0,
            Buffer = [9]
        };
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        session.SeedBackup(0, now, [frame]); // just sent, well inside the timeout

        session.Tick();

        var sent = DecodeFrames(server);
        Assert.DoesNotContain(sent, f => f.MessageIndex == 9);
        Assert.Equal(1, session.UnacknowledgedCount); // still outstanding, untouched
    }
}
