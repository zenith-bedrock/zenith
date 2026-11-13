using System.Text.RegularExpressions;
using Zenith.NetherNet.Axolotl.Utils;
using System.Globalization;

namespace Zenith.NetherNet.Axolotl.Negotiation;

/// <summary>A parsed SDP ICE candidate (port of <c>IceCandidate</c>).</summary>
public sealed partial class IceCandidate
{
    private const string TypeHost = "host";
    private const string TypeServerReflexive = "srflx";
    private const string TypeRelay = "relay";
    private const string Component = "1";

    public const int ServerReflexivePriority = (100 << 24) + (65535 << 8) + 255;

    [GeneratedRegex(@"^\S+ \d+ \S+ \d+ (\S+) \d{1,5} typ \S+(?: \S+)*\z")]
    private static partial Regex AcceptablePattern();

    public string Foundation { get; }
    public string Protocol { get; }
    public string Priority { get; }
    public string Address { get; }
    public string Port { get; }
    public string Type { get; }
    public string? RelatedAddress { get; }
    public string? RelatedPort { get; }

    public IceCandidate(string foundation, string protocol, string priority, string address, string port, string type,
        string? relatedAddress = null, string? relatedPort = null)
    {
        Foundation = foundation;
        Protocol = protocol;
        Priority = priority;
        Address = address;
        Port = port;
        Type = type;
        RelatedAddress = relatedAddress;
        RelatedPort = relatedPort;
    }

    /// <summary>Parses the value after <c>a=candidate:</c>; returns null when it is not a candidate line.</summary>
    public static IceCandidate? Parse(string value)
    {
        var parts = value.Split(' ');
        if (parts.Length < 8 || parts[6] != "typ")
            return null;

        var related = new Dictionary<string, string>();
        for (var i = 8; i + 1 < parts.Length; i += 2)
            related[parts[i]] = parts[i + 1];

        related.TryGetValue("raddr", out var raddr);
        related.TryGetValue("rport", out var rport);
        return new IceCandidate(parts[0], parts[2], parts[3], parts[4], parts[5], parts[7], raddr, rport);
    }

    /// <summary>True when a candidate sent by the peer is well-formed and names an IP address (not a hostname).</summary>
    public static bool IsAcceptableFromPeer(string value)
    {
        foreach (var prefix in new[] { "a=", "candidate:" })
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                value = value[prefix.Length..];

        var match = AcceptablePattern().Match(value.TrimEnd());
        return match.Success && InternetAddress.TryParseIp(match.Groups[1].Value, out _);
    }

    public static IceCandidate ServerReflexive(string foundation, string address, string port, int priority = ServerReflexivePriority) =>
        new(foundation, "udp", priority.ToString(CultureInfo.InvariantCulture), address, port, TypeServerReflexive);

    public IceCandidate WithoutRelatedAddress() => new(Foundation, Protocol, Priority, Address, Port, Type);

    public bool IsHost => Type == TypeHost;

    public bool IsUdpHost => IsHost && string.Equals(Protocol, "udp", StringComparison.OrdinalIgnoreCase);

    public bool IsIpv6 => Address.Contains(':');

    public override string ToString()
    {
        var parts = new List<string> { Foundation, Component, Protocol, Priority, Address, Port, "typ", Type };
        if (Type is TypeServerReflexive or TypeRelay)
        {
            parts.Add("raddr");
            parts.Add(RelatedAddress ?? (IsIpv6 ? "::" : "0.0.0.0"));
            parts.Add("rport");
            parts.Add(RelatedPort ?? "0");
        }
        return string.Join(' ', parts);
    }
}
