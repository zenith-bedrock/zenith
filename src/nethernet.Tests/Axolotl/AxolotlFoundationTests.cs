using Xunit;
using Zenith.NetherNet.Axolotl.Crypto;
using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Protocol.Framing;
using Zenith.NetherNet.Axolotl.Sdp;

namespace Zenith.NetherNet.Tests.Axolotl;

public class AxolotlFoundationTests
{
    private const string Digest = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99";

    private const string Sdp =
        "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n" +
        "a=fingerprint:sha-256 " + Digest + "\r\n" +
        "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\nc=IN IP4 1.2.3.4\r\n" +
        "a=ice-ufrag:u\r\na=ice-pwd:p\r\na=setup:passive\r\na=max-message-size:262144\r\n" +
        "a=candidate:1 1 UDP 1 1.2.3.4 5000 typ host\r\n";

    [Fact]
    public void Segmenter_and_assembler_round_trip_a_fragmented_payload()
    {
        var payload = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        var segments = new Segmenter(300).Segment(payload);
        Assert.Equal(4, segments.Count);
        Assert.Equal(3, segments[0][0]);
        Assert.Equal(0, segments[^1][0]);

        var assembler = new Assembler(true);
        byte[]? result = null;
        foreach (var s in segments)
            result = assembler.Accept(s);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void Assembler_rejects_out_of_order_and_unreliable_fragments()
    {
        var assembler = new Assembler(true);
        Assert.Null(assembler.Accept([3, 1]));
        Assert.Throws<FramingException>(() => assembler.Accept([1, 1]));
        Assert.Throws<FramingException>(() => new Assembler(false).Accept([1, 1]));
    }

    [Fact]
    public void Es384_signature_round_trips_through_der_and_jose()
    {
        var id = ServerIdentity.Generate();
        var sig = id.Sign("hello");
        Assert.Equal(96, sig.Length);
        Assert.True(id.PublicKey.Verify("hello", sig));
        Assert.False(id.PublicKey.Verify("hellO", sig));
    }

    [Fact]
    public void Private_key_pem_export_reloads_to_the_same_public_key()
    {
        var id = ServerIdentity.Generate();
        var reloaded = ServerIdentity.FromPrivateKeyPem(id.ExportPrivateKeyPem());
        Assert.Equal(id.PublicKey.ToBase64Der(), reloaded.PublicKey.ToBase64Der());
    }

    [Fact]
    public void Self_signed_assertion_verifies_against_the_covered_fingerprint_only()
    {
        var provider = new SelfSignedIdentityProvider(ServerIdentity.Generate());
        var fingerprint = Fingerprint.Parse("sha-256 " + Digest);
        var attribute = provider.Issue(fingerprint).Encode();

        var verifier = new AssertionIdentityVerifier(false, new SelfSignedTokenVerifier());
        var peer = verifier.Verify(attribute, fingerprint);
        Assert.NotNull(peer);
        Assert.Equal("self", peer!.ProviderDomain);

        var other = Fingerprint.Parse("sha-256 " + Digest.Replace("AA:", "AB:"));
        Assert.Throws<IdentityException>(() => verifier.Verify(attribute, other));
        Assert.Throws<IdentityException>(() => verifier.Verify(null, fingerprint));
        Assert.Null(new AssertionIdentityVerifier(true).Verify(null, fingerprint));
    }

    [Fact]
    public void Session_description_edits_preserve_structure()
    {
        var sdp = SessionDescription.Parse(Sdp);
        sdp.Validate();
        Assert.Equal(262144, sdp.GetMaxMessageSize());
        Assert.Equal(1, sdp.CountCandidates());

        var withId = sdp.WithIdentity("abc");
        Assert.Equal("abc", withId.GetIdentity());
        Assert.Null(withId.WithoutIdentity().GetIdentity());
        Assert.Equal(0, sdp.WithoutCandidates().CountCandidates());
        Assert.Contains("c=IN IP4 0.0.0.0", sdp.WithoutConnectionAddress().ToString());
        Assert.Equal(2, sdp.WithCandidates(["a", "b"]).CountCandidates());
        Assert.EndsWith("\r\n", sdp.ToString());
    }

    [Fact]
    public void Server_info_binary_encoding_round_trips_and_json_matches_join_contract()
    {
        var info = new ServerInfo("Zenith", 2193, "1.26.52", "World", GameType.Creative, 3, 20, Nonce: "n");
        Assert.Equal(info, ServerInfoCodec.Decode(ServerInfoCodec.Encode(info)));
        var json = ServerInfoCodec.EncodeJson(info);
        Assert.StartsWith("{\"dataVersion\":7,\"name\":\"Zenith\",\"protocol\":2193", json);
        Assert.Contains("\"transportLayer\":2", json);
    }

    [Fact]
    public void Base64Url_rejects_garbage()
    {
        Assert.Equal([1, 2, 3], Base64Url.Decode(Base64Url.Encode([1, 2, 3])));
        Assert.Throws<CryptoException>(() => Base64Url.Decode("***"));
    }
}
