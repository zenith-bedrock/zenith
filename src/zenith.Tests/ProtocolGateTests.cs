using Zenith.Protocol;
using Zenith.Packets;
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
}
