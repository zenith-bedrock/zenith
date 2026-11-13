using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;
using Xunit;
using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Protocol.Framing;
using Zenith.NetherNet.Axolotl.Sdp;
using Zenith.NetherNet.Axolotl.Server;
using Zenith.NetherNet.Axolotl.Signaling.Http;
using Zenith.NetherNet.Axolotl.Transport.WebRtc;
using Zenith.NetherNet.Axolotl.Utils;
using System.Globalization;

namespace Zenith.NetherNet.Tests.Axolotl;

public sealed class AxolotlServerIntegrationTests
{
    private sealed class Listener : IServerEventListener
    {
        public readonly TaskCompletionSource<int> Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<(byte[] Payload, Reliability Reliability)> Reliable = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<(byte[] Payload, Reliability Reliability)> Unreliable = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<DisconnectReason> Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? NetworkId;

        public void OnSessionOpen(int sessionId, string networkId, InternetAddress address, string? clientAddress, PeerIdentity? identity)
        {
            NetworkId = networkId;
            Opened.TrySetResult(sessionId);
        }

        public void OnSessionClose(int sessionId, DisconnectReason reason) => Closed.TrySetResult(reason);
        public bool CanAcceptPackets() => true;

        public void OnPacketReceive(int sessionId, byte[] payload, Reliability reliability)
        {
            (reliability == Reliability.Reliable ? Reliable : Unreliable).TrySetResult((payload, reliability));
        }

        public void OnPacketAck(int sessionId, int receiptId) { }
        public void OnBandwidthStatsUpdate(long bytesSentDiff, long bytesReceivedDiff) { }
        public void OnPingMeasure(int sessionId, int pingMs) { }
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task Port_serves_status_negotiates_over_http_and_exchanges_framed_packets()
    {
        var listener = new Listener();
        var serverIcePort = FreeUdpPort();
        var clientIcePort = FreeUdpPort();

        var configuration = new ServerConfiguration(
            new SelfSignedIdentityProvider(ServerIdentity.Generate()),
            NullNetherNetLogger.Instance,
            new AssertionIdentityVerifier(allowAnonymous: true),
            new WebRtcTransportFactory(bindAddress: "127.0.0.1", portRangeBegin: serverIcePort, portRangeEnd: serverIcePort, iceUdpMuxEnabled: true));
        var server = NetherNetServer.Create(configuration, listener);
        var info = new MutableServerInfoProvider(new ServerInfo("Zenith", 2193, "1.26.52", "world", MaxPlayerCount: 10));
        var signaling = new HttpSignaling(new InternetAddress("127.0.0.1", 0, 4), infoProvider: info);
        server.AddSignaling(signaling);
        server.Start();

        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                server.Tick();
                await Task.Delay(10);
            }
        });

        using var client = new RtcPeerConnection(new RtcPeerConfiguration
        {
            IceServers = [],
            BindAddress = "127.0.0.1",
            MaxMessageSize = Segmenter.MaxSegmentPayloadSize + 1,
            DisableAutoNegotiation = true,
            EnableIceUdpMux = false,
            PortRangeBegin = (ushort)clientIcePort,
            PortRangeEnd = (ushort)clientIcePort,
        });
        try
        {
            var baseUrl = $"http://127.0.0.1:{signaling.BoundAddress.Port}";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            var status = await http.GetStringAsync(baseUrl + "/v1/join");
            Assert.Contains("\"transportLayer\":2", status);
            Assert.Contains("\"protocol\":2193", status);

            var openMask = 0;
            var channelsOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void MarkOpen(int bit)
            {
                if ((Interlocked.Or(ref openMask, bit) | bit) == 3)
                    channelsOpen.TrySetResult();
            }

            var reliable = client.CreateDataChannel(new RtcCreateDataChannelArgs { Label = "ReliableDataChannel" });
            var unreliable = client.CreateDataChannel(new RtcCreateDataChannelArgs
            {
                Label = "UnreliableDataChannel",
                Unordered = true,
                Unreliable = true,
                MaxRetransmits = 0,
            });
            var clientAssembler = new Assembler(true);
            var clientReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            reliable.OnOpen += _ => MarkOpen(1);
            unreliable.OnOpen += _ => MarkOpen(2);
            reliable.OnBinaryReceivedSafe += (_, data) =>
            {
                var payload = clientAssembler.Accept(data.Data);
                if (payload is not null)
                    clientReceived.TrySetResult(payload);
            };

            client.SetLocalDescription(RtcDescriptionType.Offer);
            await WaitForGatheringAsync(client);

            var response = await http.PostAsync(baseUrl + "/v1/join/12345",
                new StringContent(client.LocalDescription, Encoding.UTF8, "application/sdp"));
            var answer = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, answer);

            var answerDescription = SessionDescription.Parse(answer);
            Assert.NotNull(answerDescription.GetIdentity());
            Assert.True(answerDescription.CountCandidates() > 0);

            client.SetRemoteDescription(new RtcDescription
            {
                Type = RtcDescriptionType.Answer,
                Sdp = answerDescription.WithoutIdentity().ToString(),
            });

            await channelsOpen.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var sessionId = await listener.Opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("12345", listener.NetworkId);

            // Client to server: one reliable fragmented message and one unreliable message.
            var big = Enumerable.Range(0, 40000).Select(i => (byte)(i % 251)).ToArray();
            foreach (var segment in new Segmenter(16 * 1024).Segment(big))
                reliable.Send(segment);
            unreliable.Send([0, 0xB1, 0x02]);

            var (reliablePayload, _) = await listener.Reliable.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(big, reliablePayload);
            var (unreliablePayload, _) = await listener.Unreliable.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal([0xB1, 0x02], unreliablePayload);

            // Server to client.
            server.SendPacket(sessionId, [0xC1, 0x03]);
            Assert.Equal([0xC1, 0x03], await clientReceived.Task.WaitAsync(TimeSpan.FromSeconds(10)));

            server.CloseSession(sessionId);
            reliable.Dispose();
            var reason = await listener.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(reason is DisconnectReason.PeerDisconnect or DisconnectReason.ServerDisconnect, reason.ToString());
        }
        finally
        {
            cts.Cancel();
            await pump;
            server.Shutdown();
        }
    }

    [Fact]
    public async Task Join_endpoint_rejects_bad_requests_with_the_documented_statuses()
    {
        var server = NetherNetServer.Create(
            new ServerConfiguration(new SelfSignedIdentityProvider(ServerIdentity.Generate()), NullNetherNetLogger.Instance),
            new Listener());
        var signaling = new HttpSignaling(new InternetAddress("127.0.0.1", 0, 4));
        server.AddSignaling(signaling);
        server.Start();

        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                server.Tick();
                await Task.Delay(10);
            }
        });

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var baseUrl = $"http://127.0.0.1:{signaling.BoundAddress.Port}";

            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(baseUrl + "/nope")).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed,
                (await http.PostAsync(baseUrl + "/v1/join", new StringContent("x"))).StatusCode);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType,
                (await http.PostAsync(baseUrl + "/v1/join/1", new StringContent("x", Encoding.UTF8, "text/plain"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await http.PostAsync(baseUrl + "/v1/join/abc", new StringContent("x", Encoding.UTF8, "application/sdp"))).StatusCode);

            // A syntactically valid POST with an unusable offer yields the NetherNet error code in a 500 body.
            var bad = await http.PostAsync(baseUrl + "/v1/join/1", new StringContent("not sdp", Encoding.UTF8, "application/sdp"));
            Assert.Equal(HttpStatusCode.InternalServerError, bad.StatusCode);
            Assert.Equal(((int)ErrorCode.FailedToSetRemoteDescription).ToString(CultureInfo.InvariantCulture), await bad.Content.ReadAsStringAsync());
        }
        finally
        {
            cts.Cancel();
            await pump;
            server.Shutdown();
        }
    }

    private static async Task WaitForGatheringAsync(RtcPeerConnection peer)
    {
        if (peer.GatheringState == rtcGatheringState.RTC_GATHERING_COMPLETE)
            return;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(IRtcPeerConnection _, rtcGatheringState state)
        {
            if (state == rtcGatheringState.RTC_GATHERING_COMPLETE)
                completed.TrySetResult();
        }
        peer.OnGatheringStateChange += Changed;
        try
        {
            if (peer.GatheringState != rtcGatheringState.RTC_GATHERING_COMPLETE)
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            peer.OnGatheringStateChange -= Changed;
        }
    }
}
