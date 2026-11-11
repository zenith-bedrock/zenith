using System.Net;
using System.Net.Sockets;
using Xunit;
using Zenith.Network;
using Zenith.Network.NetherNet;
using Zenith.Raknet.Stream;
using Zenith.Server;

namespace Zenith.Tests;

public sealed class NetherNetTransportTests
{
    private sealed class RecordingHost : IGameTransportHost
    {
        public int Opened;
        public bool TryOpen(IGameTransportConnection connection) { Opened++; return true; }
        public bool Receive(IGameTransportConnection connection, ref BinaryStream batch) { batch.Dispose(); return true; }
        public void Close(IGameTransportConnection connection, GameTransportCloseReason reason) { }
    }

    private sealed class NullLogger : Zenith.Raknet.Log.ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [Fact]
    public async Task Transport_serves_signaling_persists_its_identity_and_shuts_down_cleanly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zenith-nn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var signalingPort = FreePort();
        var config = new ServerConfig
        {
            Network =
            {
                Transport = "nethernet",
                NetherNet = { ListenUrl = $"http://127.0.0.1:{signalingPort}", IceBindAddress = "127.0.0.1", IceUdpMux = true }
            }
        };
        config.Validate();

        var logger = new NullLogger();
        var host = new RecordingHost();
        var transport = Assert.Single(GameTransportFactory.Create(
            config, new GameTransportEnvironment(host, logger, logger, () => 0, root, 1)));
        Assert.Equal("NetherNet", transport.Name);

        try
        {
            using var cts = new CancellationTokenSource();
            var run = transport.RunAsync(cts.Token);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            HttpResponseMessage? response = null;
            for (var attempt = 0; attempt < 50 && response is null; attempt++)
            {
                try { response = await http.GetAsync($"http://127.0.0.1:{signalingPort}/"); }
                catch (HttpRequestException) { await Task.Delay(100); }
            }

            Assert.NotNull(response);
            Assert.True(File.Exists(Path.Combine(root, GameTransportFactory.NetherNetKeyFile)));
            Assert.Equal(0, host.Opened);

            await transport.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await run.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Listen_url_resolves_wildcard_and_literal_hosts()
    {
        Assert.Equal("0.0.0.0:7552", GameTransportFactory.ParseListenUrl("http://+:7552").ToString());
        Assert.Equal("127.0.0.1:19132", GameTransportFactory.ParseListenUrl("http://127.0.0.1:19132/").ToString());
    }
}
