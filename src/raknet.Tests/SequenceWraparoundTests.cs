using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Tests;

/// <summary>
/// Cross-reference audit finding, Phase XXIII-B polish pass: <see cref="FrameSet.Sequence"/> only
/// ever carries the low 24 bits on the wire (<c>WriteTriad</c>/<c>ReadTriad</c>), so it wraps at
/// 16,777,216. The pre-fix receive-side comparison (plain numeric <c>&lt;</c>/<c>==</c>) judged
/// every FrameSet arriving after the wrap as "stale" forever, permanently stalling input — only
/// reachable after ~16.7M FrameSets on one long-lived connection, but that's ordinary for an
/// always-on server, not hypothetical.
/// </summary>
public class SequenceWraparoundTests
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

    private sealed class ProbeSession : RakNetSession
    {
        public void SetLastInputSequence(int value) => LastInputSequence = value;
    }

    private static byte[] EncodeFrameSet(uint sequence)
    {
        var frameSet = new FrameSet
        {
            Sequence = sequence,
            Packets =
            [
                new Frame
                {
                    Reliability = Reliability.Unreliable,
                    Buffer = new byte[] { (byte)MessageIdentifier.Game, 0x01 }
                }
            ]
        };
        return frameSet.Encode().ToArray();
    }

    [Fact]
    public void A_frameset_arriving_just_after_the_24_bit_wraparound_is_still_accepted()
    {
        var server = new RakNetServer(0);
        var listener = new CountingListener();
        server.SessionListener = listener;
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19138),
            Id = 1,
            Server = server,
            MTU = 1400
        };

        // Last accepted sequence is right at the top of the 24-bit space.
        session.SetLastInputSequence(0xFFFFFE);

        session.Incoming(EncodeFrameSet(0xFFFFFF)); // last sequence before the wrap
        session.Incoming(EncodeFrameSet(0));          // wrapped
        session.Incoming(EncodeFrameSet(1));          // wrapped, next

        Assert.Equal(3, listener.GamePackets);
    }

    [Fact]
    public void A_genuinely_stale_frameset_is_still_rejected_near_the_wraparound_boundary()
    {
        var server = new RakNetServer(0);
        var listener = new CountingListener();
        server.SessionListener = listener;
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19139),
            Id = 1,
            Server = server,
            MTU = 1400
        };

        session.SetLastInputSequence(5);

        // Sequence 3 is genuinely older (not a wrapped-forward value) — must still be dropped.
        session.Incoming(EncodeFrameSet(3));

        Assert.Equal(0, listener.GamePackets);
    }
}
