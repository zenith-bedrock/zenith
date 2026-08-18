using Zenith.Protocol;
using Zenith.Packets;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Xunit;

namespace Zenith.Tests;

public class ProtocolGateTests
{
    [Fact]
    public void Evaluate_equal_is_accepted()
    {
        Assert.Equal(
            ProtocolGate.Outcome.Accepted,
            ProtocolGate.Evaluate(ServerIdentity.ProtocolVersion, ServerIdentity.ProtocolVersion));
    }

    [Fact]
    public void Evaluate_client_older_fails_client()
    {
        var outcome = ProtocolGate.Evaluate(ServerIdentity.ProtocolVersion - 1, ServerIdentity.ProtocolVersion);
        Assert.Equal(ProtocolGate.Outcome.FailClient, outcome);
        Assert.Equal(PlayStatusPacket.LoginFailedClient, ProtocolGate.RejectPlayStatus(outcome));
    }

    [Fact]
    public void Evaluate_client_newer_fails_server()
    {
        var outcome = ProtocolGate.Evaluate(ServerIdentity.ProtocolVersion + 1, ServerIdentity.ProtocolVersion);
        Assert.Equal(ProtocolGate.Outcome.FailServer, outcome);
        Assert.Equal(PlayStatusPacket.LoginFailedServer, ProtocolGate.RejectPlayStatus(outcome));
    }

    [Fact]
    public void PlayStatus_failed_client_encode_shape()
    {
        var bytes = new PlayStatusPacket { Status = PlayStatusPacket.LoginFailedClient }.Encode().ToArray();
        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.PLAY_STATUS_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(PlayStatusPacket.LoginFailedClient, stream.ReadInt());
    }

    [Fact]
    public void PlayStatus_failed_server_encode_shape()
    {
        var bytes = new PlayStatusPacket { Status = PlayStatusPacket.LoginFailedServer }.Encode().ToArray();
        var stream = new BinaryStream(bytes);
        Assert.Equal((int)ProtocolInfo.PLAY_STATUS_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(PlayStatusPacket.LoginFailedServer, stream.ReadInt());
    }

    [Fact]
    public void DisconnectPacket_roundtrip_message_visible()
    {
        var original = new DisconnectPacket
        {
            Reason = DisconnectPacket.ReasonUnknown,
            HideDisconnectionScreen = false,
            Message = "Server closed",
            FilteredMessage = ""
        };
        var encoded = original.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.DISCONNECT_PACKET, stream.ReadUnsignedVarInt());

        var decoded = new DisconnectPacket();
        decoded.Decode(ref stream);
        Assert.Equal(DisconnectPacket.ReasonUnknown, decoded.Reason);
        Assert.False(decoded.HideDisconnectionScreen);
        Assert.Equal("Server closed", decoded.Message);
        Assert.Equal("", decoded.FilteredMessage);
    }

    /// <summary>
    /// Cross-reference audit finding: a protocol-mismatch rejection is only ever sent AFTER
    /// SendNetworkSettings has already run (TryAcceptProtocol is checked afterward, at both the
    /// RequestNetworkSettings and Login stages) — by then a real client has already switched into
    /// "every batch carries a leading compression-algorithm byte" mode. The pre-fix code hardcoded
    /// <c>PacketCompression.NOT_PRESENT</c> (the byte is entirely ABSENT from the stream), not
    /// <c>PacketCompression.NONE</c> (the byte IS present, valued 0xff, meaning "present but this
    /// batch wasn't actually compressed" — the correct encoding for a batch under
    /// GamePacket's compression threshold, which a tiny PlayStatus rejection always is). Sending
    /// NOT_PRESENT made a real client misread the batch's own length-prefix varint as the algorithm
    /// byte instead, crashing its decoder with "Unknown compression type 5" instead of cleanly
    /// showing the mismatch message.
    /// </summary>
    [Fact]
    public void SendIncompatibleProtocol_sends_the_algorithm_byte_the_client_now_expects()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("mismatched-client");
        player.Session.CompressionAlgorithm = PacketCompression.ZLIB; // set by HandleRequestNetworkSettings before this can ever run

        player.Session.Protocol.Login.SendIncompatibleProtocol(PlayStatusPacket.LoginFailedClient);
        player.Session.RakSession.Tick();

        var status = DecodeSolePlayStatus(fx.Transport.Captured);
        Assert.Equal(PlayStatusPacket.LoginFailedClient, status);
    }

    private static int DecodeSolePlayStatus(IEnumerable<byte[]> datagrams)
    {
        foreach (var datagram in datagrams)
        {
            if (datagram.Length < 2 || (datagram[0] & 0xf0) != (byte)BitFlags.Valid) continue;

            var frameStream = new BinaryStream(datagram[1..]);
            var frameSet = new FrameSet();
            frameSet.Decode(ref frameStream);
            frameStream.Dispose();

            foreach (var frame in frameSet.Packets)
            {
                var gameStream = new BinaryStream(frame.Buffer.ToArray());
                if (gameStream.ReadByte() != 0xfe)
                {
                    gameStream.Dispose();
                    continue;
                }

                // The algorithm byte MUST be present (this is the whole point of the fix) — a
                // below-threshold batch is still tagged NONE (0xff), never simply omitted.
                Assert.Equal(PacketCompression.NONE, gameStream.ReadByte());

                var length = gameStream.ReadUnsignedVarInt();
                var packetBytes = gameStream.ReadSpan(length).ToArray();
                gameStream.Dispose();

                var packetStream = new BinaryStream(packetBytes);
                Assert.Equal((int)ProtocolInfo.PLAY_STATUS_PACKET, packetStream.ReadUnsignedVarInt());
                var status = packetStream.ReadInt();
                packetStream.Dispose();
                return status;
            }
        }

        Assert.Fail("No PlayStatus datagram captured.");
        return -1;
    }
}
