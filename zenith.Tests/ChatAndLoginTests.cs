using Xunit;
using Zenith.Network.Protocol;
using Zenith.Network.Session;

namespace Zenith.Tests;

public class LoginIdentityTests
{
    [Fact]
    public void ExtractDisplayName_reads_xname_claim()
    {
        var jwt = MakeJwt("""{"xname":"Steve"}""");
        Assert.Equal("Steve", LoginIdentity.ExtractDisplayName(jwt));
    }

    [Fact]
    public void ExtractDisplayName_rejects_missing_xname()
    {
        var jwt = MakeJwt("""{"extraData":{}}""");
        Assert.Throws<FormatException>(() => LoginIdentity.ExtractDisplayName(jwt));
    }

    [Fact]
    public void ExtractDisplayName_rejects_empty_xname()
    {
        var jwt = MakeJwt("""{"xname":"  "}""");
        Assert.Throws<FormatException>(() => LoginIdentity.ExtractDisplayName(jwt));
    }

    [Fact]
    public void ExtractDisplayName_rejects_malformed_token()
    {
        Assert.Throws<FormatException>(() => LoginIdentity.ExtractDisplayName("not-a-jwt"));
    }

    private static string MakeJwt(string payloadJson)
    {
        static string B64Url(string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        return $"{B64Url("{}")}.{B64Url(payloadJson)}.sig";
    }
}

public class ChatProtocolTests
{
    [Fact]
    public void ClampMessage_truncates_to_max()
    {
        var longMsg = new string('a', ChatProtocol.MaxMessageLength + 10);
        var clamped = ChatProtocol.ClampMessage(longMsg);
        Assert.Equal(ChatProtocol.MaxMessageLength, clamped.Length);
    }

    [Fact]
    public void CreateChatPacket_sets_chat_type_and_fields()
    {
        var packet = ChatProtocol.CreateChatPacket("Alex", "hello");
        Assert.Equal(Zenith.Network.Packets.TextPacket.TypeChat, packet.Type);
        Assert.Equal("Alex", packet.SourceName);
        Assert.Equal("hello", packet.Message);
        Assert.False(packet.NeedsTranslation);
        Assert.Null(packet.FilteredMessage);
    }

    [Fact]
    public void CreateChatPacket_roundtrips_encode_decode()
    {
        var original = ChatProtocol.CreateChatPacket("Bob", "hi there");
        var encoded = original.Encode().ToArray();

        var stream = new Zenith.Raknet.Stream.BinaryStream(encoded);
        var header = stream.ReadUnsignedVarInt();
        Assert.Equal((int)Zenith.Network.Packets.ProtocolInfo.TEXT_PACKET, header);

        var decoded = new Zenith.Network.Packets.TextPacket();
        decoded.Decode(ref stream);

        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.SourceName, decoded.SourceName);
        Assert.Equal(original.Message, decoded.Message);
    }
}
