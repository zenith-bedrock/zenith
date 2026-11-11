using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Sdp;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Negotiation;

/// <summary>Creates and drives <see cref="TransportNegotiation"/>s with flood limits (port of <c>TransportNegotiator</c>).</summary>
public sealed class TransportNegotiator : INegotiator
{
    public const int DefaultMaxRemoteCandidates = 32;
    public const int DefaultMaxNegotiations = 256;
    public const int DefaultMaxNegotiationsPerAddress = 16;

    private readonly Dictionary<int, TransportNegotiation> _negotiations = new();
    private List<EstablishedPeer> _established = [];
    private readonly HashSet<string> _floodingAddresses = [];
    private int _nextNegotiationId;
    private bool _shutDown;

    private readonly AnswerBuilder _answerBuilder;
    private readonly AssertionIdentityVerifier _identityVerifier;
    private readonly IPeerTransportFactory _transportFactory;
    private readonly ConnectionBudgetConfiguration _budget;
    private readonly double _gatheringTimeout;
    private readonly double _channelTimeout;
    private readonly int _maxRemoteCandidates;
    private readonly int? _maxNegotiations;
    private readonly int? _maxNegotiationsPerAddress;
    private readonly INetherNetLogger _logger;

    public TransportNegotiator(
        IIdentityProvider identityProvider,
        AssertionIdentityVerifier identityVerifier,
        IPeerTransportFactory transportFactory,
        ConnectionBudgetConfiguration budget,
        double gatheringTimeout,
        double channelTimeout,
        int maxRemoteCandidates,
        int? maxNegotiations,
        int? maxNegotiationsPerAddress,
        INetherNetLogger logger,
        IReadOnlyList<string>? advertisedAddresses = null)
    {
        _identityVerifier = identityVerifier;
        _transportFactory = transportFactory;
        _budget = budget;
        _gatheringTimeout = gatheringTimeout;
        _channelTimeout = channelTimeout;
        _maxRemoteCandidates = maxRemoteCandidates;
        _maxNegotiations = maxNegotiations;
        _maxNegotiationsPerAddress = maxNegotiationsPerAddress;
        _logger = logger;
        _answerBuilder = new AnswerBuilder(identityProvider, advertisedAddresses);
    }

    public INegotiation BeginNegotiation(string offerSdp, string networkId, CandidateMode candidateMode = CandidateMode.Bundled, string? peerAddress = null)
    {
        if (_shutDown)
            throw new NegotiationException("Negotiator is shut down", ErrorCode.NoSignalingChannel);

        if (peerAddress is not null)
            peerAddress = InternetAddress.NormalizeIp(peerAddress)
                ?? throw new ArgumentException($"Peer address \"{peerAddress}\" is not an IP address");

        CheckLimits(peerAddress);

        SessionDescription offer;
        Fingerprint fingerprint;
        try
        {
            offer = SessionDescription.Parse(offerSdp);
            offer.Validate();
            fingerprint = offer.GetFingerprint();
        }
        catch (SdpException e)
        {
            throw new NegotiationException("Offer is invalid: " + e.Message, ErrorCode.FailedToSetRemoteDescription, e);
        }

        // Malformed or hostname candidates cannot be added to the transport; skipping them keeps the offer usable.
        var candidates = offer.GetSessionAttributeValues("candidate").Concat(offer.GetMediaAttributeValues("candidate")).ToList();
        var usable = candidates.Where(IceCandidate.IsAcceptableFromPeer).ToList();
        if (usable.Count != candidates.Count)
        {
            _logger.Debug($"Offer from {networkId} carries {candidates.Count - usable.Count} ICE candidates that are malformed or do not name an IP address, skipping them");
            offer = offer.WithCandidates(usable);
        }

        var candidateCount = offer.CountCandidates();
        if (candidateCount > _maxRemoteCandidates)
        {
            _logger.Debug($"Offer from {networkId} carries {candidateCount} ICE candidates, using the first {_maxRemoteCandidates}");
            offer = offer.WithCandidateLimit(_maxRemoteCandidates);
        }

        PeerIdentity? identity;
        try
        {
            identity = _identityVerifier.Verify(offer.GetIdentity(), fingerprint);
        }
        catch (IdentityException e)
        {
            throw new NegotiationException("Identity rejected: " + e.Message, ErrorCode.IdentityNotAllowed, e);
        }

        IPeerTransport transport;
        try
        {
            transport = _transportFactory.Create(_budget);
        }
        catch (TransportException e)
        {
            throw new NegotiationException("Could not create a peer connection: " + e.Message, ErrorCode.FailedToCreatePeerConnection, e);
        }

        try
        {
            transport.SetRemoteOffer(offer.WithoutIdentity().ToString());
        }
        catch (TransportException e)
        {
            transport.Close();
            throw new NegotiationException("Peer connection rejected the offer: " + e.Message, ErrorCode.FailedToSetRemoteDescription, e);
        }

        var negotiation = new TransportNegotiation(transport, networkId, peerAddress, identity, candidateMode,
            _answerBuilder, _gatheringTimeout, _channelTimeout, _maxRemoteCandidates);
        _negotiations[_nextNegotiationId++] = negotiation;

        if (peerAddress is not null)
            AddSignalingPeerCandidate(negotiation, offer, peerAddress);

        return negotiation;
    }

    private void CheckLimits(string? peerAddress)
    {
        var total = 0;
        var fromAddress = 0;
        foreach (var negotiation in _negotiations.Values)
        {
            if (negotiation.IsFinished)
                continue;
            total++;
            if (peerAddress is not null && negotiation.ClientAddress == peerAddress)
                fromAddress++;
        }

        if (peerAddress is not null && _maxNegotiationsPerAddress is not null && fromAddress >= _maxNegotiationsPerAddress)
        {
            _floodingAddresses.Add(peerAddress);
            throw new NegotiationException($"{peerAddress} already has {fromAddress} negotiations in progress", ErrorCode.IncomingConnectionIgnored);
        }
        if (_maxNegotiations is not null && total >= _maxNegotiations)
            throw new NegotiationException($"Negotiation limit of {_maxNegotiations} reached", ErrorCode.IncomingConnectionIgnored);
    }

    // The peer's address as seen by the signaling server is a candidate the offer itself may not carry
    // (for example a NATed client that only offers private host candidates).
    private void AddSignalingPeerCandidate(TransportNegotiation negotiation, SessionDescription offer, string peerAddress)
    {
        var candidate = IceCandidateFormatter.SignalingPeer(offer, peerAddress);
        if (candidate is null)
            return;
        try
        {
            negotiation.AddRemoteCandidate(candidate);
        }
        catch (NegotiationException e)
        {
            _logger.Debug($"Signaling peer candidate for {negotiation.NetworkId} was rejected: {e.Message}");
        }
    }

    public void Tick(double now)
    {
        if (_shutDown)
            return;

        foreach (var (id, negotiation) in _negotiations.ToArray())
        {
            negotiation.Update(now);
            if (!negotiation.IsFinished)
                continue;

            _negotiations.Remove(id);
            var peer = negotiation.TakeEstablishedPeer();
            if (peer is not null)
                _established.Add(peer);
            else
                _logger.Debug($"Negotiation for {negotiation.NetworkId} failed: {negotiation.FailureReason ?? "no reason given"}");
        }
    }

    public IReadOnlyList<EstablishedPeer> TakeEstablished()
    {
        var taken = _established;
        _established = [];
        return taken;
    }

    public IReadOnlyList<string> TakeFloodingAddresses()
    {
        var taken = _floodingAddresses.ToList();
        _floodingAddresses.Clear();
        return taken;
    }

    public void Shutdown()
    {
        if (_shutDown)
            return;
        _shutDown = true;

        foreach (var negotiation in _negotiations.Values)
            negotiation.Fail("Negotiator is shutting down", ErrorCode.NoSignalingChannel);
        _negotiations.Clear();

        foreach (var peer in _established)
            peer.Transport.Close();
        _established = [];
        _floodingAddresses.Clear();
    }
}
