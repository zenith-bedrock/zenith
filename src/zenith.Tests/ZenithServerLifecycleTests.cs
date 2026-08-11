using System.Net;
using System.Net.Sockets;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Server;
using Xunit;

namespace Zenith.Tests;

public sealed class ZenithServerLifecycleTests
{
    [Fact]
    public async Task Run_and_shutdown_calls_share_one_lifetime_and_cleanup_operation()
    {
        var server = CreateServer(port: 0);
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RakNetServer.OnListening = _ => listening.TrySetResult();

        var run = server.RunAsync();
        Assert.Same(run, server.RunAsync());
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var shutdown = server.ShutdownAsync();
        Assert.Same(shutdown, server.ShutdownAsync());
        await shutdown;
        await run;

        // A completed server lifetime can be observed again but never starts a second transport/loop pair.
        Assert.Same(run, server.RunAsync());
    }

    [Fact]
    public async Task Shutdown_before_run_is_idempotent_and_makes_start_invalid()
    {
        var server = CreateServer(port: 0);

        var shutdown = server.ShutdownAsync();
        Assert.Same(shutdown, server.ShutdownAsync());
        await shutdown;

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.RunAsync());
    }

    [Fact]
    public async Task Startup_failure_and_concurrent_shutdown_share_one_cleanup_operation()
    {
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
        var server = CreateServer(port);

        var run = server.RunAsync();
        var firstShutdown = server.ShutdownAsync();
        Task? secondShutdown = null;
        await Task.Run(() => secondShutdown = server.ShutdownAsync());

        Assert.NotNull(secondShutdown);
        Assert.Same(firstShutdown, secondShutdown);
        await Assert.ThrowsAnyAsync<SocketException>(() => run);
        await firstShutdown;
    }

    [Fact]
    public async Task Game_loop_failure_and_external_shutdown_share_one_cleanup_operation()
    {
        var server = CreateServer(port: 0);
        server.GameLoop.Register(new ThrowingSystem());

        var run = server.RunAsync();
        var firstShutdown = server.ShutdownAsync();
        Task? secondShutdown = null;
        await Task.Run(() => secondShutdown = server.ShutdownAsync());

        Assert.NotNull(secondShutdown);
        Assert.Same(firstShutdown, secondShutdown);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.Equal("expected game-loop failure", exception.Message);
        await firstShutdown;
    }

    private static ZenithServer CreateServer(int port) => new(
        new ServerConfig { Server = { Port = port } },
        configPath: "lifecycle-test.yml");

    private sealed class ThrowingSystem : IGameSystem
    {
        public void Tick(GameClock clock, IReadOnlyList<Player.Player> online) =>
            throw new InvalidOperationException("expected game-loop failure");
    }
}
