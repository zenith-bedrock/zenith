using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Network.Protocol;

namespace Zenith.Raknet.Tests;

/// <summary>
/// Cross-reference audit finding: nothing previously bounded how much a session's pending-send +
/// awaiting-ACK backlog could grow. A peer that stops draining (slow connection, or one that never
/// sends ACKs at all) let <c>OutputFrames</c>/<c>UnacknowledgedFrameSets</c> accumulate without limit — RAM
/// unbounded per session, same failure family <c>MAX_CONCURRENT_FRAGMENTED_MESSAGES</c> already
/// guards against on the input side. <see cref="RakNetSession"/> now disconnects a session whose
/// combined backlog exceeds a fixed budget.
/// </summary>
public class OutputBacklogBudgetTests
{
    private sealed class RecordingServer : RakNetServer
    {
        public int SendCalls { get; private set; }
        public RecordingServer() : base(port: 0) { }
        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) => SendCalls++;
    }

    [Fact]
    public void A_session_whose_peer_never_acks_is_disconnected_once_the_backlog_budget_is_exceeded()
    {
        var server = new RecordingServer();
        var session = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19140),
            Id = 1,
            Server = server,
            MTU = 1400
        };

        var payload = new byte[1000];
        var reachedClosed = false;
        for (var i = 0; i < 20_000 && !reachedClosed; i++)
        {
            session.SendFrame(new Frame
            {
                Reliability = Reliability.Reliable,
                Buffer = payload
            }, RakNetSession.Priority.Normal);
            reachedClosed = session.IsClosed;
        }

        Assert.True(reachedClosed, "session should have been disconnected once its unacked backlog exceeded the budget");
        Assert.True(server.SendCalls > 0);
    }

    [Fact]
    public void A_session_that_acks_promptly_never_hits_the_backlog_budget()
    {
        var server = new RecordingServer();
        var session = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19141),
            Id = 1,
            Server = server,
            MTU = 1400
        };

        var payload = new byte[1000];
        for (var i = 0; i < 500; i++)
        {
            session.SendFrame(new Frame
            {
                Reliability = Reliability.Reliable,
                Buffer = payload
            }, RakNetSession.Priority.Normal);

            // Simulate the peer ACKing every outstanding FrameSet immediately, keeping the backlog
            // near zero regardless of how much total traffic flows over the session's lifetime.
            var ack = new ACK { Sequences = Enumerable.Range(0, 500).Select(n => (uint)n).ToList() };
            session.Incoming(ack.Encode().ToArray());
        }

        Assert.False(session.IsClosed);
    }
}
