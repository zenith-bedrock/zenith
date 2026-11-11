using System.Net;
using System.Net.Sockets;
using Xunit;
using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Signaling;
using Zenith.NetherNet.Axolotl.Signaling.Lan;
using Zenith.NetherNet.Axolotl.Signaling.Lan.Packet;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Tests.Axolotl;

public class AxolotlLanTests
{
    [Fact]
    public void Discovery_packets_round_trip_through_the_sealed_envelope()
    {
        var message = new MessagePacket(7, 9, "CONNECTREQUEST 5 v=0");
        Assert.Equal(message, PacketSerializer.Decode(PacketSerializer.Encode(message)));

        var request = new RequestPacket(42);
        Assert.Equal(request, PacketSerializer.Decode(PacketSerializer.Encode(request)));

        var response = (ResponsePacket)PacketSerializer.Decode(PacketSerializer.Encode(new ResponsePacket(1, [1, 2, 255])));
        Assert.Equal([1, 2, 255], response.ApplicationData);
    }

    [Fact]
    public void Tampered_datagrams_are_rejected()
    {
        var frame = PacketSerializer.Encode(new RequestPacket(1));
        frame[^1] ^= 0xff;
        Assert.Throws<DiscoveryException>(() => PacketSerializer.Decode(frame));
        Assert.Throws<DiscoveryException>(() => PacketSerializer.Decode(new byte[10]));
    }

    [Fact]
    public void Signal_parsing_validates_type_and_64_bit_connection_ids()
    {
        var signal = Signal.Parse("CANDIDATEADD 000123 candidate:1 1 UDP");
        Assert.Equal(SignalType.CandidateAdd, signal.Type);
        Assert.Equal("123", signal.ConnectionId);
        Assert.Equal("candidate:1 1 UDP", signal.Data);

        Assert.Throws<SignalingException>(() => Signal.Parse("BOGUS 1 x"));
        Assert.Throws<SignalingException>(() => Signal.Parse("CONNECTREQUEST 18446744073709551616 x"));
        Assert.Throws<SignalingException>(() => Signal.Parse("CONNECTREQUEST x"));
        Assert.Equal("18446744073709551615", Signal.Parse("CONNECTREQUEST 18446744073709551615 x").ConnectionId);
    }

    [Fact]
    public async Task Lan_server_answers_a_discovery_request_with_its_advertisement()
    {
        var info = new MutableServerInfoProvider(new ServerInfo("Zenith LAN", 2193, "1.26.52", "world", PlayerCount: 1, MaxPlayerCount: 5));
        var signaling = new LanSignaling(info, 1234, new InternetAddress("127.0.0.1", 0, 4));
        var negotiator = new NoNegotiator();
        signaling.Start(new SignalingContext(negotiator, new AddressBlockTracker(), NullNetherNetLogger.Instance));
        try
        {
            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var request = PacketSerializer.Encode(new RequestPacket(99));
            await client.SendAsync(request, new IPEndPoint(IPAddress.Loopback, signaling.BoundAddress.Port));

            var receive = client.ReceiveAsync();
            for (var i = 0; i < 200 && !receive.IsCompleted; i++)
            {
                signaling.Tick(i * 0.01);
                await Task.Delay(10);
            }
            Assert.True(receive.IsCompleted, "no discovery response arrived");

            var response = Assert.IsType<ResponsePacket>(PacketSerializer.Decode((await receive).Buffer));
            Assert.Equal(1234UL, response.SenderId);
            Assert.Equal(info.GetServerInfo(), ServerInfoCodec.Decode(response.ApplicationData));
        }
        finally
        {
            signaling.Shutdown();
        }
    }

    private sealed class NoNegotiator : Zenith.NetherNet.Axolotl.Negotiation.INegotiator
    {
        public Zenith.NetherNet.Axolotl.Negotiation.INegotiation BeginNegotiation(string offerSdp, string networkId,
            Zenith.NetherNet.Axolotl.Negotiation.CandidateMode candidateMode = Zenith.NetherNet.Axolotl.Negotiation.CandidateMode.Bundled,
            string? peerAddress = null) => throw new NotSupportedException();

        public void Tick(double now) { }
        public IReadOnlyList<Zenith.NetherNet.Axolotl.Negotiation.EstablishedPeer> TakeEstablished() => [];
        public IReadOnlyList<string> TakeFloodingAddresses() => [];
        public void Shutdown() { }
    }
}
