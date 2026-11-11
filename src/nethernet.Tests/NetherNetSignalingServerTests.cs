using System.Net;
using System.Net.Http.Headers;
using Zenith.NetherNet;
using Xunit;

namespace Zenith.NetherNet.Tests;

public sealed class NetherNetSignalingServerTests
{
    private const int SampleProtocol = 1234;
    private const string SampleVersion = "test-version";

    [Fact]
    public async Task Wildcard_listener_uses_join_base_path_and_reports_windows_url_acl_denial()
    {
        var port = GetFreeTcpPort();
        var prefix = $"http://+:{port}/signaling/";
        await using var server = new NetherNetSignalingServer(
            $"http://+:{port}",
            new Uri($"http://127.0.0.1:{port}/signaling/"),
            () => new NetherNetServerInfo("Zenith", SampleProtocol, SampleVersion, "world", 2, 20, 0),
            new RecordingOfferHandler(),
            maximumOfferSize: 4096);

        Task running;
        try
        {
            running = server.StartAsync();
        }
        catch (InvalidOperationException exception) when (
            OperatingSystem.IsWindows() &&
            exception.InnerException is HttpListenerException { NativeErrorCode: 5 })
        {
            Assert.Contains(prefix, exception.Message);
            Assert.Contains("netsh http add urlacl", exception.Message);
            Assert.Contains("administrator", exception.Message, StringComparison.OrdinalIgnoreCase);
            return;
        }

        using var client = new HttpClient();
        using var info = await client.GetAsync($"http://127.0.0.1:{port}/signaling/v1/join");
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.Contains($"\"protocol\":{SampleProtocol}", await info.Content.ReadAsStringAsync());

        await server.DisposeAsync();
        await running;
    }

    [Fact]
    public async Task Get_join_returns_server_metadata_and_post_returns_sdp_answer()
    {
        var port = GetFreeTcpPort();
        var handler = new RecordingOfferHandler();
        await using var server = new NetherNetSignalingServer(
            new Uri($"http://127.0.0.1:{port}/"),
            new NetherNetServerInfo("Zenith", SampleProtocol, SampleVersion, "world", 2, 20, 0),
            handler,
            maximumOfferSize: 4096);
        var running = server.StartAsync();
        using var client = new HttpClient();

        var info = await client.GetAsync($"http://127.0.0.1:{port}/v1/join");
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.Contains($"\"protocol\":{SampleProtocol}", await info.Content.ReadAsStringAsync());
        Assert.Contains($"\"version\":\"{SampleVersion}\"", await info.Content.ReadAsStringAsync());

        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/join/client%2Fone")
        {
            Content = new StringContent("v=0\r\n", System.Text.Encoding.UTF8, "application/sdp")
        };
        var answer = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("application/sdp", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("v=0\r\nanswer", await answer.Content.ReadAsStringAsync());
        Assert.Equal("client/one", handler.NetworkId);
        Assert.Equal("v=0\r\n", handler.Offer);

        await server.DisposeAsync();
        await running;
    }

    [Fact]
    public async Task Post_rejects_wrong_content_type_and_oversized_offer()
    {
        var port = GetFreeTcpPort();
        await using var server = new NetherNetSignalingServer(
            new Uri($"http://127.0.0.1:{port}/"),
            new NetherNetServerInfo("Zenith", SampleProtocol, SampleVersion, "world", 0, 20, 0),
            new RecordingOfferHandler(),
            maximumOfferSize: 4);
        var running = server.StartAsync();
        using var client = new HttpClient();

        using var wrongType = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/join/id")
        {
            Content = new StringContent("offer", System.Text.Encoding.UTF8, "text/plain")
        };
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.SendAsync(wrongType)).StatusCode);

        using var tooLarge = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/join/id")
        {
            Content = new StringContent("offer", System.Text.Encoding.UTF8, "application/sdp")
        };
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(tooLarge)).StatusCode);

        await server.DisposeAsync();
        await running;
    }

    [Fact]
    public async Task Post_rejects_empty_or_invalid_utf8_offer()
    {
        var port = GetFreeTcpPort();
        await using var server = new NetherNetSignalingServer(
            new Uri($"http://127.0.0.1:{port}/"),
            new NetherNetServerInfo("Zenith", SampleProtocol, SampleVersion, "world", 0, 20, 0),
            new RecordingOfferHandler(),
            maximumOfferSize: 4096);
        var running = server.StartAsync();
        using var client = new HttpClient();

        using var empty = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/join/id")
        {
            Content = new ByteArrayContent([])
        };
        empty.Content.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(empty)).StatusCode);

        using var invalidUtf8 = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/join/id")
        {
            Content = new ByteArrayContent([0xff])
        };
        invalidUtf8.Content.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidUtf8)).StatusCode);

        await server.DisposeAsync();
        await running;
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent_and_waits_for_inflight_offer()
    {
        var port = GetFreeTcpPort();
        var handler = new BlockingOfferHandler();
        await using var server = new NetherNetSignalingServer(
            new Uri($"http://127.0.0.1:{port}/"),
            new NetherNetServerInfo("Zenith", SampleProtocol, SampleVersion, "world", 0, 20, 0),
            handler,
            maximumOfferSize: 4096);
        var running = server.StartAsync();
        using var client = new HttpClient();
        var post = client.PostAsync(
            $"http://127.0.0.1:{port}/v1/join/id",
            new StringContent("v=0\r\n", System.Text.Encoding.UTF8, "application/sdp"));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var firstDispose = server.DisposeAsync().AsTask();
        var secondDispose = server.DisposeAsync().AsTask();
        try
        {
            Assert.False(firstDispose.IsCompleted);
            Assert.False(secondDispose.IsCompleted);
        }
        finally
        {
            handler.Release.TrySetResult("v=0\r\nanswer");
        }

        await Task.WhenAll(firstDispose, secondDispose);
        await running;
        try { using var response = await post; }
        catch (HttpRequestException) { }
    }

    [Fact]
    public async Task StopAccepting_is_safe_after_disposal()
    {
        var port = GetFreeTcpPort();
        await using var server = new NetherNetSignalingServer(
            new Uri($"http://127.0.0.1:{port}/"),
            new NetherNetServerInfo("Zenith", SampleProtocol, SampleVersion, "world", 0, 20, 0),
            new RecordingOfferHandler(),
            maximumOfferSize: 4096);
        var running = server.StartAsync();

        await server.DisposeAsync();
        server.StopAccepting();
        await running;
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class RecordingOfferHandler : INetherNetOfferHandler
    {
        public string? NetworkId { get; private set; }
        public string? Offer { get; private set; }

        public Task<string?> CreateAnswerAsync(string networkId, string offerSdp, CancellationToken cancellationToken)
        {
            NetworkId = networkId;
            Offer = offerSdp;
            return Task.FromResult<string?>("v=0\r\nanswer");
        }
    }

    private sealed class BlockingOfferHandler : INetherNetOfferHandler
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string?> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string?> CreateAnswerAsync(string networkId, string offerSdp, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            return await Release.Task;
        }
    }
}
