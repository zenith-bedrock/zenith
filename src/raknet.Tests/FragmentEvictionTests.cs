using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;

namespace Zenith.Raknet.Tests;

/// <summary>
/// Cross-reference audit finding, Phase XXIII-B polish pass: a split id whose peer never delivers
/// the final fragment (lost fragment, disconnect mid-transfer) occupied its reassembly slot forever
/// — no timeout existed. After <c>MAX_CONCURRENT_FRAGMENTED_MESSAGES</c> (32) such abandoned
/// reassemblies accumulated over a session's lifetime, the session could no longer receive ANY
/// further split packet. <see cref="RakNetSession.HandleFragment"/> now sweeps stale entries first.
/// </summary>
public class FragmentEvictionTests
{
    private sealed class NullListener : IRakNetSessionListener
    {
        public bool HandleGamePacket(RakNetSession session, ref Zenith.Raknet.Stream.BinaryStream stream)
        {
            stream.Dispose();
            return true;
        }
    }

    private sealed class ProbeSession : RakNetSession
    {
        public void SeedFragment(short id, long startedAtMs, Dictionary<int, Frame> parts) =>
            FragmentsQueue[id] = (startedAtMs, parts);

        public int FragmentQueueCount => FragmentsQueue.Count;
    }

    [Fact]
    public void An_abandoned_reassembly_older_than_the_fragment_timeout_is_evicted_on_the_next_fragment()
    {
        var server = new RakNetServer(0);
        server.SessionListener = new NullListener();
        var session = new ProbeSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19137),
            Id = 1,
            Server = server,
            MTU = 1400
        };

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // A stale, abandoned reassembly (only 1 of 3 parts ever arrived) from well past the 30s timeout.
        session.SeedFragment(
            id: 1,
            startedAtMs: now - 60_000,
            parts: new Dictionary<int, Frame>
            {
                [0] = new Frame { Reliability = Reliability.ReliableOrdered, MessageIndex = 0, Buffer = [1] }
            });
        Assert.Equal(1, session.FragmentQueueCount);

        // Any new incoming fragment (a different, unrelated split id) triggers the sweep.
        var ok = session.HandleFrameForTests(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            MessageIndex = 100,
            OrderIndex = 0,
            OrderChannel = 0,
            SplitInfo = new Frame.SplitPacketInfo(Count: 2, Id: 2, Index: 0),
            Buffer = [0xFE, 0xAA]
        });

        Assert.False(ok); // the new split isn't complete yet either
        // The stale id=1 entry is gone; only the new id=2 (still incomplete) reassembly remains.
        Assert.Equal(1, session.FragmentQueueCount);
    }
}
