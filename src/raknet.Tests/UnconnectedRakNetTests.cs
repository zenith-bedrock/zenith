using System.Collections.Concurrent;
using System.Net;
using Xunit;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Tests;

file sealed class RecordingRakNetServer : RakNetServer
{
    public ConcurrentQueue<byte[]> Captured { get; } = new();

    public RecordingRakNetServer(uint maxConnections = 20, uint maxConnectionsPerAddress = 3)
        : base(port: 0)
    {
        MaxConnections = maxConnections;
        MaxConnectionsPerAddress = maxConnectionsPerAddress;
    }

    public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) =>
        Captured.Enqueue(buffer.ToArray());
}

/// <summary>
/// <see cref="UnconnectedRakNet.Handle"/> — MTU clamping, connection caps, and retransmission
/// safety had zero test coverage before this file.
/// </summary>
public sealed class UnconnectedRakNetTests
{
    private static readonly IPEndPoint Remote = new(IPAddress.Loopback, 19132);

    private static byte[] BuildOpenConnectionRequest1(int requestedMtu)
    {
        // OpenConnectionRequest1.Decode derives MTUSize from (stream.Length - 17) — the client pads
        // the datagram to signal the MTU it wants to try, it doesn't write a length field.
        var totalAfterPid = requestedMtu + 17;
        var buffer = new byte[1 + totalAfterPid];
        buffer[0] = (byte)MessageIdentifier.OpenConnectionRequest1;
        // bytes[1..17) magic (unvalidated by ReadMagic), byte[17] protocol, rest padding — all zero is fine.
        return buffer;
    }

    private static byte[] BuildOpenConnectionRequest2(ushort mtu, ulong clientGuid)
    {
        var writer = new BinaryStream();
        writer.WriteByte((byte)MessageIdentifier.OpenConnectionRequest2);
        writer.WriteMagic();
        writer.WriteIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
        writer.WriteUShort(mtu);
        writer.WriteULong(clientGuid);
        return writer.TakeOwnedBuffer();
    }

    /// <summary>Reply layout: [pid(1)][magic(16)][guid(8)][useSecurity(1)][mtu(2)], UseSecurity always false here.</summary>
    private static ushort ReadReply1Mtu(byte[] reply)
    {
        var stream = new BinaryStream(reply, 26, 2);
        return stream.ReadUShort();
    }

    [Fact]
    public void Request1_below_minimum_mtu_is_clamped_up_to_400()
    {
        var server = new RecordingRakNetServer();
        var handler = new UnconnectedRakNet(server);

        // Requested MTU=100 (+28 added by the handler) would be 128, well below MIN_MTU=400.
        handler.Handle(Remote, BuildOpenConnectionRequest1(100));

        var reply = Assert.Single(server.Captured);
        Assert.Equal(400, ReadReply1Mtu(reply));
    }

    [Fact]
    public void Request1_above_maximum_mtu_is_clamped_down_to_1492()
    {
        var server = new RecordingRakNetServer();
        var handler = new UnconnectedRakNet(server);

        handler.Handle(Remote, BuildOpenConnectionRequest1(5000));

        var reply = Assert.Single(server.Captured);
        Assert.Equal(1492, ReadReply1Mtu(reply));
    }

    [Fact]
    public void Request1_within_range_is_preserved_plus_the_28_byte_header_allowance()
    {
        var server = new RecordingRakNetServer();
        var handler = new UnconnectedRakNet(server);

        handler.Handle(Remote, BuildOpenConnectionRequest1(1000));

        var reply = Assert.Single(server.Captured);
        Assert.Equal(1028, ReadReply1Mtu(reply));
    }

    [Fact]
    public void Request2_creates_exactly_one_session_and_replies()
    {
        var server = new RecordingRakNetServer();
        var handler = new UnconnectedRakNet(server);

        handler.Handle(Remote, BuildOpenConnectionRequest2(600, clientGuid: 42));

        Assert.Equal(1, server.ConnectionCount);
        Assert.True(server.HasSession(Remote));
        Assert.Single(server.Captured);
    }

    /// <summary>Common under UDP packet loss: the client retries Request2 before seeing the first
    /// reply. Must resend, not create (and leak) a second session or re-fire OnSessionOpen.</summary>
    [Fact]
    public void Request2_retransmission_for_an_existing_session_resends_without_duplicating()
    {
        var server = new RecordingRakNetServer();
        var handler = new UnconnectedRakNet(server);

        handler.Handle(Remote, BuildOpenConnectionRequest2(600, clientGuid: 42));
        handler.Handle(Remote, BuildOpenConnectionRequest2(600, clientGuid: 42));

        Assert.Equal(1, server.ConnectionCount);
        Assert.Equal(2, server.Captured.Count);
    }

    [Fact]
    public void Request2_is_rejected_once_the_server_is_at_max_connections()
    {
        var server = new RecordingRakNetServer(maxConnections: 1);
        var handler = new UnconnectedRakNet(server);
        handler.Handle(new IPEndPoint(IPAddress.Loopback, 1), BuildOpenConnectionRequest2(600, clientGuid: 1));
        Assert.Equal(1, server.ConnectionCount);

        handler.Handle(new IPEndPoint(IPAddress.Loopback, 2), BuildOpenConnectionRequest2(600, clientGuid: 2));

        // Rejected: no new session, and no reply sent for the rejected attempt (only the first accept's reply exists).
        Assert.Equal(1, server.ConnectionCount);
        Assert.Single(server.Captured);
    }
}
