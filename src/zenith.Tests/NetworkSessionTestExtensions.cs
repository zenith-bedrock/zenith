using Zenith.Network.RakNet;
using Zenith.Raknet;
using Zenith.Session;

namespace Zenith.Tests;

/// <summary>Test access to the RakNet session behind a <see cref="NetworkSession"/> built on a RakNet connection.</summary>
internal static class NetworkSessionTestExtensions
{
    public static RakNetSession RakSession(this NetworkSession session) =>
        ((RakNetGameConnection)session.Transport).Session;
}
