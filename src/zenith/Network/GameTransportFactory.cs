using System.Net;
using Zenith.NetherNet.Axolotl.Info;
using Zenith.NetherNet.Axolotl.Signaling.Http;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Transport.WebRtc;
using Zenith.NetherNet.Axolotl.Utils;
using Zenith.Network.NetherNet;
using Zenith.Network.RakNet;
using Zenith.Raknet;
using Zenith.Raknet.Log;
using Zenith.Server;

namespace Zenith.Network;

/// <summary>What the transports need from the running server, without handing them the whole server.</summary>
internal sealed record GameTransportEnvironment(
    IGameTransportHost Host,
    ILogger ServerLogger,
    ILogger RakNetLogger,
    Func<int> OnlinePlayerCount,
    string PersistentRoot,
    ulong RakNetGuid);

/// <summary>Builds the transports <c>network.transport</c> asks for. The only place that reads transport configuration.</summary>
internal static class GameTransportFactory
{
    public const string NetherNetKeyFile = "nethernet-operator-key.pk8";

    public static IReadOnlyList<IGameTransport> Create(ServerConfig config, GameTransportEnvironment environment)
    {
        var transports = new List<IGameTransport>();
        if (config.Network.UsesRakNet)
            transports.Add(CreateRakNet(config, environment));
        if (config.Network.UsesNetherNet)
            transports.Add(CreateNetherNet(config, environment));
        return transports;
    }

    private static RakNetTransport CreateRakNet(ServerConfig config, GameTransportEnvironment environment)
    {
        var server = new RakNetServer(config.Server.Port, environment.RakNetGuid, config.Network.SocketBufferBytes)
        {
            Logger = environment.RakNetLogger,
            MaxConnections = (uint)config.Server.MaxPlayers,
            MaxConnectionsPerAddress = (uint)config.Server.MaxPlayersPerIp,
            Motd = config.Server.Motd,
            SubMotd = config.Server.SubMotd,
            ListGameMode = config.Server.Gamemode,
            ProtocolVersion = ServerIdentity.ProtocolVersion,
            VersionName = ServerIdentity.VersionName,
            OnlinePlayerCount = environment.OnlinePlayerCount,
            OnListening = _ => environment.ServerLogger.Info("Server ready.")
        };
        return new RakNetTransport(environment.Host, server);
    }

    private static NetherNetTransport CreateNetherNet(ServerConfig config, GameTransportEnvironment environment)
    {
        var nn = config.Network.NetherNet;
        var logger = new NetherNetLogAdapter(environment.ServerLogger);
        var joinUri = new Uri(nn.JoinUrl, UriKind.Absolute);

        var auth = config.Auth;
        var franchise = auth.AllowsXbox
            ? new FranchiseTokenVerifier(
                required: !(auth.AllowsOfflineFallback || auth.AllowsSelfSigned),
                reason => environment.ServerLogger.Warning($"NetherNet GameServerToken rejected: {reason}"))
            : null;

        var iceServers = nn.IceServers.Select(IceServer.Parse).ToList();
        var webRtc = new WebRtcTransportFactory(
            iceServers,
            string.IsNullOrWhiteSpace(nn.IceBindAddress) ? null : nn.IceBindAddress,
            nn.IcePortRangeStart, nn.IcePortRangeEnd, nn.IceUdpMux, nn.Mtu);

        var settings = new NetherNetTransportSettings(
            SignalingAddress: ParseListenUrl(nn.ListenUrl),
            Tls: nn.TlsCertificate.Length == 0
                ? null
                : new TlsConfiguration(nn.TlsCertificate, nn.TlsPrivateKey.Length == 0 ? null : nn.TlsPrivateKey),
            ReverseProxy: nn.ReverseProxyHeader.Length == 0
                ? null
                : new ReverseProxy(nn.ReverseProxyHeader, nn.ReverseProxyNetworks),
            LanAddress: nn.LanDiscovery ? new InternetAddress("0.0.0.0", Zenith.NetherNet.Axolotl.Signaling.Lan.LanSignaling.DefaultPort, 4) : null,
            Identity: Zenith.NetherNet.Axolotl.Identity.ServerIdentity.LoadOrCreate(Path.Combine(environment.PersistentRoot, NetherNetKeyFile)),
            IdentityDomain: joinUri.Host,
            FranchiseVerifier: franchise,
            AllowAnonymous: auth.AllowsOfflineFallback || auth.AllowsSelfSigned,
            WebRtc: webRtc,
            Budget: new ConnectionBudgetConfiguration(maxMessageSize: nn.MaxMessageSize),
            AdvertisedAddresses: nn.AdvertiseAddresses.Count == 0 ? null : nn.AdvertiseAddresses,
            Advertisement: () => new ServerInfo(
                config.Server.Motd,
                Zenith.Server.ServerIdentity.ProtocolVersion,
                Zenith.Server.ServerIdentity.VersionName,
                config.World.Name,
                string.Equals(config.Server.Gamemode, "Creative", StringComparison.Ordinal) ? GameType.Creative : GameType.Survival,
                environment.OnlinePlayerCount(),
                config.Server.MaxPlayers,
                AcceptsOnlineAuth: auth.AllowsXbox,
                AcceptsSelfSignedAuth: auth.AllowsSelfSigned || auth.AllowsOfflineFallback));

        environment.ServerLogger.Info(
            $"NetherNet signaling: {nn.JoinUrl} (listen {nn.ListenUrl}); " +
            (nn.IceUdpMux
                ? $"ICE UDP mux on port {nn.IcePortRangeStart}"
                : $"ICE UDP ports {nn.IcePortRangeStart}-{nn.IcePortRangeEnd}"));
        return new NetherNetTransport(environment.Host, settings, logger);
    }

    /// <summary><c>http://+:port</c> binds every IPv4 interface; otherwise the host is resolved to its first address.</summary>
    internal static InternetAddress ParseListenUrl(string listenUrl)
    {
        const string wildcard = "http://+:";
        if (listenUrl.StartsWith(wildcard, StringComparison.OrdinalIgnoreCase))
            return new InternetAddress("0.0.0.0", int.Parse(listenUrl[wildcard.Length..].TrimEnd('/')), 4);

        var uri = new Uri(listenUrl, UriKind.Absolute);
        var address = IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal)
            ? literal
            : Dns.GetHostAddresses(uri.Host).First();
        return new InternetAddress(address.ToString(), uri.Port, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 6 : 4);
    }

    private sealed class NetherNetLogAdapter(ILogger logger) : INetherNetLogger
    {
        public void Debug(string message) => logger.Debug("[NetherNet] " + message);
        public void Info(string message) => logger.Info("[NetherNet] " + message);
        public void Warning(string message) => logger.Warning("[NetherNet] " + message);
        public void Error(string message) => logger.Error("[NetherNet] " + message);
    }
}
