using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Tests;

/// <summary>
/// <see cref="RakNetSession.SendFrame"/>'s split loop now slices <c>Frame.Buffer</c>
/// (<c>ReadOnlyMemory&lt;byte&gt;</c>) instead of copying each fragment into its own <c>byte[]</c>.
/// A GC-owned-array slice has no lifetime risk on its own (the backing array stays alive as long as
/// any slice references it), but this proves the actual end-to-end behavior didn't change: a large
/// payload sent through real fragmentation, captured as raw datagrams, and fed into a receiving
/// session must still reassemble to byte-for-byte the original — not just that the code compiles.
/// </summary>
public class OutboundFragmentSlicingRoundTripTests
{
    private sealed class RecordingServer : RakNetServer
    {
        public List<byte[]> Captured { get; } = [];
        public RecordingServer() : base(port: 0) { }
        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) => Captured.Add(buffer.ToArray());
    }

    private sealed class CapturingListener : IRakNetSessionListener
    {
        public byte[]? LastGameBody { get; private set; }
        public bool HandleGamePacket(RakNetSession session, ref BinaryStream stream)
        {
            LastGameBody = stream.Buffer[stream.Offset..stream.Length].ToArray();
            stream.Dispose();
            return true;
        }
    }

    [Fact]
    public void A_large_reliable_send_slices_fragments_without_corrupting_the_reassembled_payload()
    {
        var sendServer = new RecordingServer();
        var sender = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19142),
            Id = 1,
            Server = sendServer,
            MTU = 576
        };

        var body = new byte[5000];
        for (var i = 0; i < body.Length; i++)
            body[i] = (byte)(i % 251); // identifiable, non-repeating-enough pattern to catch misordering

        var full = new byte[1 + body.Length];
        full[0] = (byte)MessageIdentifier.Game;
        body.CopyTo(full, 1);

        sender.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = 0,
            Buffer = full
        }, RakNetSession.Priority.Immediate);

        Assert.True(sendServer.Captured.Count > 1, "5000-byte payload over a 576 MTU should have split into multiple datagrams");

        var recvServer = new RecordingServer();
        var listener = new CapturingListener();
        recvServer.SessionListener = listener;
        var receiver = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19143),
            Id = 2,
            Server = recvServer,
            MTU = 576
        };

        foreach (var datagram in sendServer.Captured)
            receiver.Incoming(datagram);

        Assert.NotNull(listener.LastGameBody);
        Assert.Equal(body, listener.LastGameBody);
    }
}
