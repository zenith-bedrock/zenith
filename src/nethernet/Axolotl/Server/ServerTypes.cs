using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Negotiation;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Transport.WebRtc;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Server;

public enum ServerState
{
    Created,
    Running,
    Stopping,
    Stopped,
}

/// <summary>Callbacks the embedding game server implements (port of <c>ServerEventListener</c>).</summary>
public interface IServerEventListener
{
    void OnSessionOpen(int sessionId, string networkId, InternetAddress address, string? clientAddress, PeerIdentity? identity);
    void OnSessionClose(int sessionId, DisconnectReason reason);

    /// <summary>False pauses packet delivery (back-pressure) until the listener can take more.</summary>
    bool CanAcceptPackets();

    void OnPacketReceive(int sessionId, byte[] payload, Reliability reliability);
    void OnPacketAck(int sessionId, int receiptId);
    void OnBandwidthStatsUpdate(long bytesSentDiff, long bytesReceivedDiff);
    void OnPingMeasure(int sessionId, int pingMs);
}

public interface IServerInterface
{
    void SendPacket(int sessionId, ReadOnlySpan<byte> payload, Reliability reliability = Reliability.Reliable, int? receiptId = null);
    void CloseSession(int sessionId, DisconnectReason reason = DisconnectReason.ServerDisconnect);
    void BlockAddress(string address, int timeout = 300);
    void UnblockAddress(string address);
}

/// <summary>Everything needed to build a <see cref="NetherNetServer"/> (port of <c>ServerConfiguration</c>).</summary>
public sealed class ServerConfiguration
{
    public const double DefaultGatheringTimeout = 15.0;
    public const double DefaultChannelTimeout = 5.0;

    public IIdentityProvider IdentityProvider { get; }
    public INetherNetLogger Logger { get; }
    public AssertionIdentityVerifier IdentityVerifier { get; }
    public IPeerTransportFactory TransportFactory { get; }
    public ConnectionBudgetConfiguration Budget { get; }
    public double GatheringTimeout { get; }
    public double ChannelTimeout { get; }
    public int MaxRemoteCandidates { get; }
    public int? MaxNegotiations { get; }
    public int? MaxNegotiationsPerAddress { get; }
    public IReadOnlyList<string>? AdvertisedAddresses { get; }

    public ServerConfiguration(
        IIdentityProvider identityProvider,
        INetherNetLogger logger,
        AssertionIdentityVerifier? identityVerifier = null,
        IPeerTransportFactory? transportFactory = null,
        ConnectionBudgetConfiguration? budget = null,
        double gatheringTimeout = DefaultGatheringTimeout,
        double channelTimeout = DefaultChannelTimeout,
        int maxRemoteCandidates = TransportNegotiator.DefaultMaxRemoteCandidates,
        int? maxNegotiations = TransportNegotiator.DefaultMaxNegotiations,
        int? maxNegotiationsPerAddress = TransportNegotiator.DefaultMaxNegotiationsPerAddress,
        IReadOnlyList<string>? advertisedAddresses = null)
    {
        if (maxRemoteCandidates < 1)
            throw new ArgumentException($"Maximum remote candidates must be positive, got {maxRemoteCandidates}");
        if (maxNegotiations is < 1)
            throw new ArgumentException($"Maximum negotiations must be positive or null, got {maxNegotiations}");
        if (maxNegotiationsPerAddress is < 1)
            throw new ArgumentException($"Maximum negotiations per address must be positive or null, got {maxNegotiationsPerAddress}");
        if (maxNegotiations is not null && maxNegotiationsPerAddress is not null && maxNegotiationsPerAddress > maxNegotiations)
            throw new ArgumentException($"Maximum negotiations per address ({maxNegotiationsPerAddress}) cannot be greater than the maximum negotiations ({maxNegotiations})");
        if (gatheringTimeout <= 0.0 || channelTimeout <= 0.0)
            throw new ArgumentException("Timeouts must be positive");
        foreach (var address in advertisedAddresses ?? [])
            if (InternetAddress.NormalizeIp(address) is null)
                throw new ArgumentException($"Advertised address \"{address}\" is not an IP address");

        IdentityProvider = identityProvider;
        Logger = logger;
        IdentityVerifier = identityVerifier ?? new AssertionIdentityVerifier();
        TransportFactory = transportFactory ?? new WebRtcTransportFactory();
        Budget = budget ?? new ConnectionBudgetConfiguration();
        GatheringTimeout = gatheringTimeout;
        ChannelTimeout = channelTimeout;
        MaxRemoteCandidates = maxRemoteCandidates;
        MaxNegotiations = maxNegotiations;
        MaxNegotiationsPerAddress = maxNegotiationsPerAddress;
        AdvertisedAddresses = advertisedAddresses;
    }
}
