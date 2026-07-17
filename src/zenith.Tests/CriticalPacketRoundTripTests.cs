using Xunit;
using Zenith.Packets;
using Zenith.Raknet.Stream;

namespace Zenith.Tests;

public class CriticalPacketRoundTripTests
{
    [Fact]
    public void UpdateBlock_roundtrips_coords_and_runtime_id()
    {
        var original = new UpdateBlockPacket
        {
            X = 10,
            Y = -60,
            Z = -3,
            BlockRuntimeId = 12345,
            Flags = UpdateBlockPacket.FlagNeighborsAndNetwork,
            DataLayerId = 0
        };

        var encoded = original.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.UPDATE_BLOCK_PACKET, stream.ReadUnsignedVarInt());

        var decoded = new UpdateBlockPacket();
        decoded.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(original.X, decoded.X);
        Assert.Equal(original.Y, decoded.Y);
        Assert.Equal(original.Z, decoded.Z);
        Assert.Equal(original.BlockRuntimeId, decoded.BlockRuntimeId);
        Assert.Equal(original.Flags, decoded.Flags);
        Assert.Equal(original.DataLayerId, decoded.DataLayerId);
    }

    [Fact]
    public void Login_roundtrips_protocol_auth_and_client_jwt()
    {
        var original = new LoginPacket
        {
            Protocol = 1001,
            AuthInfo = new LoginPacket.AuthenticationInfo
            {
                AuthenticationType = LoginPacket.AuthenticationInfo.TypeSelfSigned,
                Certificate = null,
                Token = ""
            },
            ClientDataJwt = "eyJhbGciOiJub25lIn0.e30."
        };

        var encoded = original.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.LOGIN_PACKET, stream.ReadUnsignedVarInt());

        var decoded = new LoginPacket();
        decoded.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(original.Protocol, decoded.Protocol);
        Assert.Equal(original.AuthInfo.AuthenticationType, decoded.AuthInfo.AuthenticationType);
        Assert.Equal(original.ClientDataJwt, decoded.ClientDataJwt);
        // Certificate may round-trip as null or omitted — accept either
        Assert.True(
            decoded.AuthInfo.Certificate is null ||
            decoded.AuthInfo.Certificate == "");
    }
}
