using Xunit;
using Zenith.Protocol;
using Zenith.Server;
using Zenith.Session;

namespace Zenith.Tests;

public class LoginIdentityTests
{
    private static ServerConfig.AuthSection Auth(params string[] modes)
    {
        var a = new ServerConfig.AuthSection { Accept = [.. modes] };
        a.NormalizeAndValidate();
        return a;
    }

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
    public void ParseIdentityToken_xbox_only_rejects_empty_xname()
    {
        var jwt = MakeJwt("""{"xname":""}""");
        Assert.Throws<FormatException>(() =>
            LoginIdentity.ParseIdentityToken(jwt, auth: Auth("xbox")));
    }

    [Fact]
    public void ParseIdentityToken_falls_back_to_chain_displayName_when_offline()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var chainJwt = MakeJwt(
            "{\"extraData\":{\"displayName\":\"OfflineSteve\",\"identity\":\"" + id + "\"}}");
        var chain = "{\"chain\":[\"" + chainJwt + "\"]}";
        var token = MakeJwt("""{"xname":""}""");

        var parsed = LoginIdentity.ParseIdentityToken(token, chain, auth: Auth("xbox", "self-signed", "offline"));
        Assert.Equal("OfflineSteve", parsed.DisplayName);
        Assert.Equal(id, parsed.Uuid);
        Assert.True(parsed.IdentityStable);
    }

    [Fact]
    public void ParseIdentityToken_falls_back_to_ThirdPartyName_and_SelfSignedId()
    {
        var selfId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var token = MakeJwt("""{"xname":""}""");
        var clientData = MakeJwt(
            "{\"ThirdPartyName\":\"GuestAlex\",\"SelfSignedId\":\"" + selfId + "\"}");

        var parsed = LoginIdentity.ParseIdentityToken(
            token, identityChainJson: null, clientData, Auth("xbox", "offline"));
        Assert.Equal("GuestAlex", parsed.DisplayName);
        Assert.Equal(selfId, parsed.Uuid);
        Assert.True(parsed.IdentityStable);
    }

    [Fact]
    public void ParseIdentityToken_offline_stable_uuid_from_name()
    {
        var token = MakeJwt("""{"xname":"LANPlayer"}""");
        var auth = Auth("xbox", "offline");
        var a = LoginIdentity.ParseIdentityToken(token, auth: auth);
        var b = LoginIdentity.ParseIdentityToken(token, auth: auth);
        Assert.Equal(a.Uuid, b.Uuid);
        Assert.True(a.IdentityStable);
        Assert.Equal(LoginIdentity.IdentityFromOfflineName("LANPlayer"), a.Uuid);
    }

    [Fact]
    public void ParseIdentityToken_reads_leguuid_self_signed()
    {
        var id = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var jwt = MakeJwt("{\"xname\":\"Steve\",\"leguuid\":\"" + id + "\"}");
        var parsed = LoginIdentity.ParseIdentityToken(jwt, auth: Auth("self-signed"));
        Assert.Equal(id, parsed.Uuid);
        Assert.True(parsed.IdentityFromJwt);
        Assert.True(parsed.IdentityStable);
    }

    [Fact]
    public void ValidateIdentityChain_soft_skips_empty_dummy_jwt()
    {
        LoginIdentity.ValidateIdentityChain("""{"chain":[""]}""", requireStrictXbox: false);
    }

    [Fact]
    public void ValidateIdentityChain_strict_rejects_empty_dummy_jwt()
    {
        Assert.Throws<FormatException>(() =>
            LoginIdentity.ValidateIdentityChain("""{"chain":[""]}""", requireStrictXbox: true));
    }

    [Fact]
    public void ExtractDisplayName_rejects_malformed_token()
    {
        Assert.Throws<FormatException>(() => LoginIdentity.ExtractDisplayName("not-a-jwt"));
    }

    [Fact]
    public void ParseIdentityToken_xbox_only_marks_ephemeral_without_identity_or_xid()
    {
        var jwt = MakeJwt("""{"xname":"Steve"}""");
        var parsed = LoginIdentity.ParseIdentityToken(jwt, auth: Auth("xbox"));
        Assert.False(parsed.IdentityFromJwt);
        Assert.False(parsed.IdentityStable);
        Assert.NotEqual(Guid.Empty, parsed.Uuid);
    }

    [Fact]
    public void ParseIdentityToken_marks_jwt_uuid_when_identity_claim_present()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var jwt = MakeJwt("{\"xname\":\"Steve\",\"identity\":\"" + id + "\"}");
        var parsed = LoginIdentity.ParseIdentityToken(jwt, auth: Auth("xbox"));
        Assert.True(parsed.IdentityFromJwt);
        Assert.True(parsed.IdentityStable);
        Assert.Equal(id, parsed.Uuid);
    }

    [Fact]
    public void ParseIdentityToken_derives_stable_uuid_from_xid()
    {
        var jwt = MakeJwt("""{"xname":"Steve","xid":"25332747913222912"}""");
        var a = LoginIdentity.ParseIdentityToken(jwt, auth: Auth("xbox"));
        var b = LoginIdentity.ParseIdentityToken(jwt, auth: Auth("xbox"));
        Assert.False(a.IdentityFromJwt);
        Assert.True(a.IdentityStable);
        Assert.Equal(a.Uuid, b.Uuid);
        Assert.Equal(LoginIdentity.IdentityFromXuid("25332747913222912"), a.Uuid);
    }

    [Fact]
    public void ParseIdentityToken_identity_claim_takes_priority_over_xid()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var jwt = MakeJwt(
            "{\"xname\":\"Steve\",\"identity\":\"" + id + "\",\"xid\":\"25332747913222912\"}");
        var parsed = LoginIdentity.ParseIdentityToken(jwt, auth: Auth("xbox"));
        Assert.Equal(id, parsed.Uuid);
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
        var longMsg = new string('a', 512 + 10);
        var clamped = ChatProtocol.ClampMessage(longMsg, maxLength: 512);
        Assert.Equal(512, clamped.Length);
    }

    [Fact]
    public void CreateChatPacket_sets_chat_type_and_fields()
    {
        var packet = ChatProtocol.CreateChatPacket("Alex", "hello");
        Assert.Equal(Zenith.Packets.TextPacket.TypeChat, packet.Type);
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
        Assert.Equal((int)Zenith.Packets.ProtocolInfo.TEXT_PACKET, header);

        var decoded = new Zenith.Packets.TextPacket();
        decoded.Decode(ref stream);

        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.SourceName, decoded.SourceName);
        Assert.Equal(original.Message, decoded.Message);
    }
}
