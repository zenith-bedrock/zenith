using Zenith.NetherNet.Axolotl.Identity;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Sdp;

namespace Zenith.NetherNet.Axolotl.Negotiation;

/// <summary>Turns the transport's local description into the signed answer (port of <c>AnswerBuilder</c>).</summary>
public sealed class AnswerBuilder(IIdentityProvider identityProvider, IReadOnlyList<string>? advertisedAddresses = null)
{
    public LocalAnswer Build(string localDescription, CandidateMode candidateMode)
    {
        var answer = SessionDescription.Parse(localDescription);
        var ufragValues = answer.GetMediaAttributeValues("ice-ufrag");
        var ufrag = ufragValues.Count > 0 ? ufragValues[0] : throw new SdpException("Local description has no ice-ufrag");

        // Advertised addresses only rewrite bundled answers; trickle ICE is used for LAN discovery where
        // local gathered addresses are required.
        if (candidateMode == CandidateMode.Trickle)
        {
            answer = answer.WithoutCandidates();
        }
        else if (advertisedAddresses is not null)
        {
            // The transport writes its default candidate's address to the connection line.
            answer = answer
                .WithCandidates(IceCandidateFormatter.Advertise(answer.GetMediaAttributeValues("candidate"), advertisedAddresses))
                .WithoutConnectionAddress();
        }

        if (candidateMode == CandidateMode.Bundled && answer.CountCandidates() == 0)
            throw new NegotiationException("No local candidates to answer with", ErrorCode.Ice);

        var assertion = identityProvider.Issue(answer.GetFingerprint());
        return new LocalAnswer(answer.WithIdentity(assertion.Encode()).ToString(), ufrag);
    }
}
