using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Sdp;
using Zenith.NetherNet.Axolotl.Transport;

namespace Zenith.NetherNet.Axolotl.Negotiation;

/// <summary>State machine for a single offer: gather, answer, wait for both data channels (port of <c>TransportNegotiation</c>).</summary>
public sealed class TransportNegotiation(
    IPeerTransport transport,
    string networkId,
    string? clientAddress,
    PeerIdentity? identity,
    CandidateMode candidateMode,
    AnswerBuilder answerBuilder,
    double gatheringTimeout,
    double channelTimeout,
    int maxRemoteCandidates) : INegotiation
{
    private NegotiationState _state = NegotiationState.Gathering;
    private double? _deadline;
    private LocalAnswer? _answer;
    private string? _failureReason;
    private ErrorCode _failureCode = ErrorCode.None;
    private int _candidateIndex;
    private int _remoteCandidateCount;
    private readonly Dictionary<Reliability, IMessageChannel> _channels = new();
    private EstablishedPeer? _establishedPeer;

    public string NetworkId => networkId;
    public string? ClientAddress => clientAddress;
    public string? Answer => _answer?.Sdp;
    public bool IsFinished => _state.IsFinished();
    public bool IsFailed => _state == NegotiationState.Failed;
    public bool IsClosed => _state == NegotiationState.Failed || transport.State.IsBroken();
    public string? FailureReason => _failureReason;
    public ErrorCode FailureCode => _failureCode;

    public void AddRemoteCandidate(string candidate)
    {
        if (_state.IsFinished())
            return;
        if (_remoteCandidateCount >= maxRemoteCandidates)
            return;
        _remoteCandidateCount++;

        if (!IceCandidate.IsAcceptableFromPeer(candidate))
            throw new NegotiationException("Peer sent a candidate that is malformed or does not name an IP address", ErrorCode.CandidateAdd);

        try
        {
            transport.AddRemoteCandidate(candidate);
        }
        catch (TransportException e)
        {
            throw new NegotiationException("Peer sent an unusable candidate: " + e.Message, ErrorCode.CandidateAdd, e);
        }
    }

    public IReadOnlyList<string> TakeLocalCandidates()
    {
        var answer = _answer;
        if (answer is null || _state.IsFinished() || candidateMode == CandidateMode.Bundled)
            return [];

        IReadOnlyList<string> gathered;
        try
        {
            gathered = transport.PollLocalCandidates();
        }
        catch (TransportException)
        {
            return [];
        }

        var formatted = new List<string>();
        foreach (var candidate in gathered)
        {
            try
            {
                formatted.Add(IceCandidateFormatter.Format(candidate, answer.IceUfrag, _candidateIndex++));
            }
            catch (NegotiationException)
            {
                // Skip candidates the formatter rejects.
            }
        }
        return formatted;
    }

    public void Fail(string reason, ErrorCode code = ErrorCode.GenericFailure)
    {
        if (_state.IsFinished())
            return;
        _state = NegotiationState.Failed;
        _failureReason = reason;
        _failureCode = code;
        foreach (var channel in _channels.Values)
            channel.Close();
        transport.Close();
    }

    public void Close()
    {
        if (!_state.IsFinished())
        {
            Fail("Closed");
            return;
        }
        transport.Close();
    }

    public void Update(double now)
    {
        if (_state.IsFinished())
            return;

        var transportState = transport.State;
        if (transportState.IsBroken())
        {
            Fail("Peer connection entered state " + transportState, ErrorCode.Ice);
            return;
        }

        try
        {
            switch (_state)
            {
                case NegotiationState.Gathering:
                    UpdateGathering(now);
                    break;
                case NegotiationState.Answered:
                    UpdateAnswered(now);
                    break;
            }
        }
        catch (TransportException e)
        {
            Fail("Peer connection failure: " + e.Message,
                _state == NegotiationState.Gathering ? ErrorCode.FailedToCreateAnswer : ErrorCode.Ice);
        }
    }

    public EstablishedPeer? TakeEstablishedPeer()
    {
        var peer = _establishedPeer;
        _establishedPeer = null;
        return peer;
    }

    private void UpdateGathering(double now)
    {
        if (candidateMode == CandidateMode.Bundled && !transport.IsGatheringComplete)
        {
            FailIfTimedOut(now, gatheringTimeout, "Timed out gathering ICE candidates", ErrorCode.Ice);
            return;
        }

        var localDescription = transport.GetLocalDescription();
        if (localDescription is null)
        {
            FailIfTimedOut(now, gatheringTimeout, "Timed out waiting for a local description", ErrorCode.FailedToCreateAnswer);
            return;
        }

        try
        {
            _answer = answerBuilder.Build(localDescription, candidateMode);
        }
        catch (SdpException e)
        {
            Fail("Could not build an answer: " + e.Message, ErrorCode.FailedToCreateAnswer);
            return;
        }
        catch (IdentityException e)
        {
            Fail("Could not issue an identity assertion: " + e.Message, ErrorCode.FailedToCreateIdentityAssertion);
            return;
        }
        catch (NegotiationException e)
        {
            Fail(e.Message, e.ErrorCode);
            return;
        }

        Enter(NegotiationState.Answered);
    }

    private void UpdateAnswered(double now)
    {
        foreach (var channel in transport.PollChannels())
        {
            var reliability = channel.Reliability;
            if (reliability is null)
            {
                channel.Close();
                Fail("Unsupported data channel: " + channel.Label, ErrorCode.DataChannelClosed);
                return;
            }
            if (_channels.ContainsKey(reliability.Value))
            {
                channel.Close();
                Fail("Duplicate data channel opened: " + reliability.Value.GetChannelLabel(), ErrorCode.DataChannelClosed);
                return;
            }
            _channels[reliability.Value] = channel;
        }

        _channels.TryGetValue(Reliability.Reliable, out var reliable);
        _channels.TryGetValue(Reliability.Unreliable, out var unreliable);
        if (reliable is null || unreliable is null || !reliable.IsOpen || !unreliable.IsOpen)
        {
            FailIfTimedOut(now, channelTimeout, "Peer did not open both data channels", ErrorCode.NegotiationTimeoutWaitingForAccept);
            return;
        }

        var address = transport.RemoteAddress;
        if (address is null)
        {
            Fail("Data channels opened without a selected candidate pair", ErrorCode.Ice);
            return;
        }

        _establishedPeer = new EstablishedPeer(transport, new ChannelPair(reliable, unreliable), networkId, address, clientAddress, identity);
        Enter(NegotiationState.Established);
    }

    private void Enter(NegotiationState state)
    {
        _state = state;
        _deadline = null;
    }

    private void FailIfTimedOut(double now, double timeout, string reason, ErrorCode code)
    {
        _deadline ??= now + timeout;
        if (now >= _deadline)
            Fail(reason, code);
    }
}
