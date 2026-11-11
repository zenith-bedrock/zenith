using System.Net;
using Zenith.Network;
using Zenith.Network.RakNet;
using Zenith.Raknet;
using Xunit;

namespace Zenith.Tests;

public sealed class SessionLifecycleTests
{
    [Fact]
    public void Session_open_after_stop_accepting_is_closed_without_host_deadlock()
    {
        var fx = new IntentTestFixture();
        var host = new GameSessionHost(fx.Context);
        var transport = new RakNetTransport(host, new NoopRakNetServer());
        host.StopAccepting();
        var session = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19133),
            Id = 1,
            Server = transport.Server,
            MTU = 1400
        };

        transport.OnSessionOpen(session);

        Assert.True(session.IsClosed);
    }

    private sealed class NoopRakNetServer : RakNetServer
    {
        public NoopRakNetServer() : base(port: 0) { }

        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) { }
    }
}
