using System.Net;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Session;
using Xunit;

namespace Zenith.Tests;

public sealed class SessionLifecycleTests
{
    [Fact]
    public void Session_open_after_stop_accepting_is_closed_without_listener_deadlock()
    {
        var fx = new IntentTestFixture();
        var transport = new NoopRakNetServer();
        var listener = new ZenithSessionListener(fx.Context);
        transport.SessionListener = listener;
        listener.StopAccepting();
        var session = new RakNetSession
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 19133),
            Id = 1,
            Server = transport,
            MTU = 1400
        };

        listener.OnSessionOpen(session);

        Assert.True(session.IsClosed);
    }

    private sealed class NoopRakNetServer : RakNetServer
    {
        public NoopRakNetServer() : base(port: 0) { }

        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer) { }
    }
}
