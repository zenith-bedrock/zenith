using Xunit;
using System.Net;
using System.Net.Sockets;

namespace Zenith.Raknet.Tests;

public sealed class RakNetServerLifetimeTests
{
    [Fact]
    public async Task Shutdown_is_idempotent_and_drains_the_running_loops()
    {
        var server = new RakNetServer(port: 0);
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnListening = _ => listening.TrySetResult();
        var run = server.StartAsync();
        Assert.Same(run, server.StartAsync());

        await listening.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var shutdown = server.ShutdownAsync();
        Assert.Same(shutdown, server.ShutdownAsync());
        await shutdown;

        await run;
        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Startup_bind_failure_can_be_shutdown_without_leaving_a_transport_task()
    {
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
        var server = new RakNetServer(port);

        var run = server.StartAsync();
        var shutdown = server.ShutdownAsync();
        Assert.Same(shutdown, server.ShutdownAsync());

        await Assert.ThrowsAnyAsync<SocketException>(() => run);
        await shutdown;
    }

    [Fact]
    public async Task Shutdown_before_start_is_terminal_and_start_is_rejected()
    {
        var server = new RakNetServer(port: 0);

        var shutdown = server.ShutdownAsync();
        Assert.Same(shutdown, server.ShutdownAsync());
        await shutdown;

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
    }
}
