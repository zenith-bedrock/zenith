using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Sdp;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Negotiation;

/// <summary>Candidate rewriting helpers (port of <c>IceCandidateFormatter</c>).</summary>
public static class IceCandidateFormatter
{
    private const string Prefix = "candidate:";
    private const int AdvertisedFoundation = 80000000;

    /// <summary>
    /// A server-reflexive candidate for the address the signaling request came from, reusing the offer's UDP host port.
    /// Null when the offer already names that address or has no UDP host candidate.
    /// </summary>
    public static string? SignalingPeer(SessionDescription offer, string address)
    {
        string? port = null;
        foreach (var value in offer.GetSessionAttributeValues("candidate").Concat(offer.GetMediaAttributeValues("candidate")))
        {
            var candidate = IceCandidate.Parse(value);
            if (candidate is null)
                continue;
            if ((InternetAddress.NormalizeIp(candidate.Address) ?? candidate.Address) == address)
                return null;
            if (port is null && candidate.IsUdpHost)
                port = candidate.Port;
        }
        if (port is null)
            return null;
        return Prefix + IceCandidate.ServerReflexive("signaling", address, port);
    }

    /// <summary>
    /// Rewrites a gathered candidate list to expose only the operator-advertised addresses (NAT / public IP support):
    /// host candidates on unlisted addresses are dropped, other types lose their related address, and any listed
    /// address with no host candidate is announced as server-reflexive on the gathered UDP host port.
    /// </summary>
    public static List<string> Advertise(IEnumerable<string> candidates, IEnumerable<string> addresses)
    {
        var addressList = addresses.Select(a => InternetAddress.NormalizeIp(a) ?? a).Distinct().ToList();
        var kept = new List<string>();
        var matched = new HashSet<string>();
        var ports = new Dictionary<bool, string>();

        foreach (var value in candidates)
        {
            var candidate = IceCandidate.Parse(value);
            if (candidate is null)
                continue;

            if (candidate.IsUdpHost)
                ports.TryAdd(candidate.IsIpv6, candidate.Port);

            var address = InternetAddress.NormalizeIp(candidate.Address) ?? candidate.Address;
            var listed = addressList.Contains(address);
            if (listed)
                matched.Add(address);

            if (!candidate.IsHost)
                kept.Add(candidate.WithoutRelatedAddress().ToString());
            else if (listed)
                kept.Add(value);
        }

        var announced = 0;
        foreach (var address in addressList)
        {
            if (!ports.TryGetValue(address.Contains(':'), out var port) || matched.Contains(address))
                continue;
            kept.Add(IceCandidate.ServerReflexive(
                (AdvertisedFoundation + announced).ToString(),
                address,
                port,
                IceCandidate.ServerReflexivePriority - (announced << 8)).ToString());
            announced++;
        }
        return kept;
    }

    /// <summary>Formats a locally gathered candidate for the trickle signaling message.</summary>
    public static string Format(string candidate, string ufrag, int index)
    {
        if (candidate.StartsWith("a=", StringComparison.Ordinal))
            candidate = candidate[2..];
        if (!candidate.StartsWith(Prefix, StringComparison.Ordinal))
            throw new NegotiationException($"Candidate does not start with \"{Prefix}\": {candidate}", ErrorCode.CandidateAdd);

        var parsed = IceCandidate.Parse(candidate[Prefix.Length..])
            ?? throw new NegotiationException($"Candidate is not in the expected form: {candidate}", ErrorCode.CandidateAdd);
        return Prefix + parsed + $" generation 0 ufrag {ufrag} network-id {index} network-cost 0";
    }
}
