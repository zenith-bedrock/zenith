using System.Text;
using Zenith.Binary;
using Zenith.Packets;
using Zenith.Session.Handler;
using Xunit;

namespace Zenith.Tests;

public class LoginBoundaryTests
{
    // Independent body: BE protocol 2225; unsigned request length 10; LE JSON length 2;
    // JSON {}; LE ClientData length 0. No use of LoginPacket.Encode to build the oracle.
    private static byte[] Body => Convert.FromHexString("000008B10A020000007B7D00000000");

    [Fact]
    public void Login_reads_independent_unsigned_length_vector()
    {
        var packet = Decode(Body);
        Assert.Equal(2225, packet.Protocol); Assert.Empty(packet.ClientDataJwt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(11)]
    public void Login_rejects_inconsistent_request_window(byte length)
    {
        var bytes = Body; bytes[4] = length;
        Assert.Throws<InvalidOperationException>(() => Decode(bytes));
    }

    [Fact]
    public void Login_rejects_trailing_packet_bytes() =>
        Assert.Throws<InvalidDataException>(() => Decode([.. Body, 0]));

    [Fact]
    public void Encoder_uses_actual_unsigned_request_length()
    {
        var r = new BinaryStream(new LoginPacket().Encode().ToArray());
        Assert.Equal(1, r.ReadUnsignedVarInt()); _ = r.ReadInt();
        Assert.Equal(r.Length - r.Offset - 1, r.Buffer[r.Offset]); // this small request fits one unsigned byte
        var length = r.ReadUnsignedVarInt(); Assert.Equal(r.Length - r.Offset, length);
        r.Dispose();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(256)]
    [InlineData(0)] // FULL without issuer/client proof, even with Certificate absent
    public void Invalid_authentication_closes_before_identity_acceptance(int authenticationType)
    {
        var fx = new IntentTestFixture();
        fx.Context.Config.Auth.Accept = ["xbox"];
        fx.Context.Config.Auth.NormalizeAndValidate();
        var peer = fx.AddPlayer("connection");
        var packet = new LoginPacket
        {
            Protocol = 2225,
            AuthInfo = new LoginPacket.AuthenticationInfo
            {
                AuthenticationType = authenticationType,
                Token = "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"xname\":\"Claimed Player\",\"xid\":\"1234\"}")).TrimEnd('=') + ".AA"
            }
        };
        var r = new BinaryStream(packet.Encode().ToArray()); _ = r.ReadUnsignedVarInt();
        new LoginSessionHandler().HandleDataPacket(peer.Session, new DataPacket.HeaderInfo { Id = 1 }, ref r);
        Assert.True(peer.Session.IsClosed); Assert.False(peer.Session.HasGameIdentity);
        Assert.Single(fx.Players.Online); Assert.Null(fx.Players.Get("Claimed Player"));
        r.Dispose();
    }

    private static LoginPacket Decode(byte[] body)
    {
        var r = new BinaryStream(body);
        try { var packet = new LoginPacket(); packet.Decode(ref r); return packet; }
        finally { r.Dispose(); }
    }
}
