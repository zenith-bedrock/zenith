using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Negotiation;

public class NegotiationException : NetherNetException
{
    public ErrorCode ErrorCode { get; }

    public NegotiationException(string message, ErrorCode errorCode = ErrorCode.GenericFailure, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}

public enum CandidateMode
{
    /// <summary>All candidates are gathered before answering and carried in the answer.</summary>
    Bundled,

    /// <summary>The answer carries no candidates; they are exchanged incrementally.</summary>
    Trickle,
}

public enum NegotiationState
{
    Gathering,
    Answered,
    Established,
    Failed,
}

public static class NegotiationStateExtensions
{
    public static bool IsFinished(this NegotiationState state) => state is NegotiationState.Established or NegotiationState.Failed;
}

public sealed record LocalAnswer(string Sdp, string IceUfrag);

public sealed record EstablishedPeer(
    IPeerTransport Transport,
    ChannelPair Channels,
    string NetworkId,
    InternetAddress Address,
    string? ClientAddress,
    PeerIdentity? Identity);

/// <summary>An in-flight offer/answer exchange (port of <c>Negotiation</c>).</summary>
public interface INegotiation
{
    string? Answer { get; }
    bool IsFinished { get; }
    bool IsFailed { get; }
    bool IsClosed { get; }
    string? FailureReason { get; }
    ErrorCode FailureCode { get; }
    void AddRemoteCandidate(string candidate);
    IReadOnlyList<string> TakeLocalCandidates();
    void Fail(string reason, ErrorCode code = ErrorCode.GenericFailure);
    void Close();
}

public interface INegotiator
{
    INegotiation BeginNegotiation(string offerSdp, string networkId, CandidateMode candidateMode = CandidateMode.Bundled, string? peerAddress = null);
    void Tick(double now);
    IReadOnlyList<EstablishedPeer> TakeEstablished();
    IReadOnlyList<string> TakeFloodingAddresses();
    void Shutdown();
}
