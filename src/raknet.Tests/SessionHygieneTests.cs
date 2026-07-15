using System.Net;
using Xunit;
using Zenith.Raknet;

namespace Zenith.Raknet.Tests;

public class SessionHygieneTests
{
    [Fact]
    public void MotdOnlineCount_uses_provider_not_connection_count()
    {
        var server = CreateServer();
        AddGhost(server, "127.0.0.1", 1000);
        AddGhost(server, "127.0.0.1", 1001);
        Assert.Equal(2, server.ConnectionCount);

        server.OnlinePlayerCount = () => 1;
        Assert.Equal(1, server.MotdOnlineCount);
    }

    [Fact]
    public void TryEvictSessionForAddress_prefers_unbound_over_HasGameIdentity()
    {
        var server = CreateServer(maxPerAddress: 2);
        var bound = AddGhost(server, "10.0.0.5", 2000);
        bound.HasGameIdentity = true;
        bound.LastSeen = 1_000;
        var ghost = AddGhost(server, "10.0.0.5", 2001);
        ghost.HasGameIdentity = false;
        ghost.LastSeen = 2_000; // newer, but unbound

        Assert.True(server.TryEvictSessionForAddress(IPAddress.Parse("10.0.0.5"), out var victim));
        Assert.Same(ghost, victim);
        Assert.Equal(1, server.ConnectionCount);
        Assert.True(bound.HasGameIdentity);
    }

    [Fact]
    public void TryEvictSessionForAddress_falls_back_to_oldest_LastSeen()
    {
        var server = CreateServer(maxPerAddress: 2);
        var older = AddGhost(server, "10.0.0.6", 3000);
        older.HasGameIdentity = true;
        older.LastSeen = 100;
        var newer = AddGhost(server, "10.0.0.6", 3001);
        newer.HasGameIdentity = true;
        newer.LastSeen = 200;

        Assert.True(server.TryEvictSessionForAddress(IPAddress.Parse("10.0.0.6"), out var victim));
        Assert.Same(older, victim);
        Assert.Equal(1, server.ConnectionCount);
    }

    private static RakNetServer CreateServer(uint maxPerAddress = 3) =>
        new(0)
        {
            MaxConnectionsPerAddress = maxPerAddress,
            MaxConnections = 20
        };

    private static RakNetSession AddGhost(RakNetServer server, string ip, int port)
    {
        var session = new RakNetSession
        {
            Id = server.NextSessionId(),
            EndPoint = new IPEndPoint(IPAddress.Parse(ip), port),
            MTU = 1400,
            Server = server
        };
        server.AddSession(session);
        return session;
    }
}
