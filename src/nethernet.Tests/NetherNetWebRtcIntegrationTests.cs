using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;
using Xunit;
using Zenith.NetherNet;

namespace Zenith.NetherNet.Tests;

public sealed class NetherNetWebRtcIntegrationTests
{
    [Fact]
    public async Task Full_ice_negotiation_opens_both_channels_and_bridges_frames()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"zenith-nethernet-rtc-{Guid.NewGuid():N}.pk8");
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        using var serverIcePortProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverIcePort = ((IPEndPoint)serverIcePortProbe.Client.LocalEndPoint!).Port;
        using var clientIcePortProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var clientIcePort = ((IPEndPoint)clientIcePortProbe.Client.LocalEndPoint!).Port;
        serverIcePortProbe.Dispose();
        clientIcePortProbe.Dispose();

        await using var host = new NetherNetHost(
            $"http://127.0.0.1:{port}",
            new Uri($"http://127.0.0.1:{port}"),
            () => new NetherNetServerInfo("Zenith", 2193, "1.26.52", "world", 0, 10, 0),
            NetherNetServerIdentity.LoadOrCreate(keyPath, "127.0.0.1"),
            validateClientToken: null,
            allowAnonymous: true,
            allowUnverifiedToken: true,
            maximumMessageSize: 1024 * 1024,
            maximumFragmentSize: 16 * 1024,
            maximumConnections: 2,
            iceBindAddress: "127.0.0.1",
            icePortRangeBegin: (ushort)serverIcePort,
            icePortRangeEnd: (ushort)serverIcePort);

        var connectionOpened = new TaskCompletionSource<NetherNetConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new ConcurrentDictionary<byte, TaskCompletionSource<byte[]>>();
        received[0xA1] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        received[0xB1] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionOpened += connection =>
        {
            connection.GameBatchReceived += (_, batch) =>
            {
                if (batch.Length > 0 && received.TryGetValue(batch[0], out var completion))
                    completion.TrySetResult(batch);
            };
            connectionOpened.TrySetResult(connection);
        };

        using var client = new RtcPeerConnection(new RtcPeerConfiguration
        {
            IceServers = [],
            BindAddress = "127.0.0.1",
            MaxMessageSize = 16 * 1024 + 1,
            DisableAutoNegotiation = true,
            EnableIceUdpMux = false,
            PortRangeBegin = (ushort)clientIcePort,
            PortRangeEnd = (ushort)clientIcePort
        });
        var peerStates = new ConcurrentQueue<string>();
        RtcLog.Initialize(rtcLogLevel.RTC_LOG_INFO, (level, message) => peerStates.Enqueue($"native[{level}]={message}"));
        client.OnConnectionStateChange += (_, state) => peerStates.Enqueue($"peer={state}");
        client.OnIceStateChange += (_, state) => peerStates.Enqueue($"ice={state}");
        try
        {
            var openMask = 0;
            var channelsOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                MaxRetransmits = 0
            });
            reliable.OnOpen += _ => MarkOpen(1);
            unreliable.OnOpen += _ => MarkOpen(2);
            reliable.OnBinaryReceivedSafe += (_, data) => clientReceived.TrySetResult(data.Data.ToArray());

            client.SetLocalDescription(RtcDescriptionType.Offer);
            await WaitForIceGatheringAsync(client);
            var offerSdp = client.LocalDescription;
            Assert.Contains("a=candidate:", offerSdp, StringComparison.Ordinal);
            var answer = await host.CreateAnswerAsync("network-id-opaque", offerSdp, CancellationToken.None);
            Assert.NotNull(answer);
            Assert.Contains("a=identity:", answer, StringComparison.Ordinal);
            Assert.Contains("a=candidate:", answer, StringComparison.Ordinal);

            client.SetRemoteDescription(new RtcDescription
            {
                Type = RtcDescriptionType.Answer,
                Sdp = NetherNetSdpIdentity.RemoveIdentity(answer)
            });

            var serverConnection = await connectionOpened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await channelsOpen.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException exception)
            {
                var offerCandidates = string.Join(" | ", offerSdp.Split("\r\n").Where(line => line.StartsWith("a=candidate:", StringComparison.Ordinal)));
                var answerCandidates = string.Join(" | ", answer.Split("\r\n").Where(line => line.StartsWith("a=candidate:", StringComparison.Ordinal)));
                throw new TimeoutException($"WebRTC timed out opening channels; client={client.ConnectionState}/{client.IceState}, server={serverConnection.WebRtcConnectionState}/{serverConnection.WebRtcIceState}, serverClosed={serverConnection.IsClosed}, serverReason={serverConnection.CloseReason}, serverOpen={serverConnection.AreDataChannelsOpen}, candidates={offerCandidates} <> {answerCandidates}, states={string.Join(" | ", peerStates.TakeLast(12))}", exception);
            }
            Assert.True(serverConnection.AreDataChannelsOpen);
            Assert.True(serverConnection.SendReliable([0xC1, 0x03]), serverConnection.CloseReason);
            Assert.Equal([0, 0xC1, 0x03], await clientReceived.Task.WaitAsync(TimeSpan.FromSeconds(10)));

            var reliableFrame = NetherNetFrameCodec.EncodeReliable([0xA1, 0x01], 16 * 1024, 1024 * 1024).Single();
            var unreliableFrame = NetherNetFrameCodec.EncodeUnreliable([0xB1, 0x02], 1024 * 1024);
            reliable.Send(reliableFrame);
            unreliable.Send(unreliableFrame);

            Assert.Equal([0xA1, 0x01], await received[0xA1].Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal([0xB1, 0x02], await received[0xB1].Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(serverConnection.IsClosed);

            var connectionClosed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            serverConnection.Closed += (_, reason) => connectionClosed.TrySetResult(reason);
            reliable.Dispose();
            await connectionClosed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(serverConnection.IsClosed);
            Assert.False(serverConnection.SendReliable([0xA1]));
            await serverConnection.DisposeAsync();
        }
        finally
        {
            if (File.Exists(keyPath))
                File.Delete(keyPath);
        }
    }

    private static async Task WaitForIceGatheringAsync(RtcPeerConnection peer)
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
