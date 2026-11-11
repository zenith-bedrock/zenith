using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Signaling.Http;

/// <summary>Trusted reverse proxies and the header that carries the real client address (port of <c>ReverseProxy</c>).</summary>
public sealed partial class ReverseProxy
{
    public const string HeaderForwardedFor = "X-Forwarded-For";
    public const string HeaderRealIp = "X-Real-IP";
    public const string HeaderCfConnectingIp = "CF-Connecting-IP";
    private const string HeaderForwarded = "Forwarded";

    private static readonly byte[] Ipv4MappedPrefix = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff];

    [GeneratedRegex(@"^(\d+\.\d+\.\d+\.\d+):\d+\z")]
    private static partial Regex Ipv4WithPort();

    private readonly List<(byte[] Network, int Prefix)> _networks = [];
    private readonly bool _list;

    public string Header { get; }

    public ReverseProxy(string header, IReadOnlyList<string> networks)
    {
        if (!HttpRequest.TokenPattern().IsMatch(header))
            throw new ArgumentException($"\"{header}\" is not a valid header name");
        if (string.Equals(header, HeaderForwarded, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The {HeaderForwarded} header is not supported");
        if (networks.Count == 0)
            throw new ArgumentException("At least one trusted proxy network must be specified");

        foreach (var network in networks)
            _networks.Add(ParseNetwork(network));

        Header = header;
        _list = string.Equals(header, HeaderForwardedFor, StringComparison.OrdinalIgnoreCase);
    }

    public bool Trusts(string address)
    {
        if (!TryPack(address, out var packed))
            return false;
        return _networks.Any(n => InNetwork(packed, n.Network, n.Prefix));
    }

    /// <summary>The client address named by the proxy header (for X-Forwarded-For: the right-most untrusted entry).</summary>
    public string? ClientAddress(HttpRequest request)
    {
        var value = request.GetHeader(Header);
        if (value is null)
            return null;
        if (!_list)
            return Normalize(value);

        string? address = null;
        foreach (var entry in value.Split(',').Reverse())
        {
            address = Normalize(entry);
            if (address is null || !Trusts(address))
                break;
        }
        return address;
    }

    private static bool TryPack(string address, out byte[] packed)
    {
        packed = [];
        if (!InternetAddress.TryParseIp(address, out var ip))
            return false;
        packed = ip.GetAddressBytes();
        return true;
    }

    private static string? Normalize(string entry)
    {
        entry = entry.Trim();
        if (entry.StartsWith('['))
        {
            var close = entry.IndexOf(']');
            if (close < 0)
                return null;
            entry = entry[1..close];
        }
        else
        {
            var match = Ipv4WithPort().Match(entry);
            if (match.Success)
                entry = match.Groups[1].Value;
        }

        if (!TryPack(entry, out var packed))
            return null;
        if (packed.Length == 16 && packed.AsSpan(0, Ipv4MappedPrefix.Length).SequenceEqual(Ipv4MappedPrefix))
            packed = packed[Ipv4MappedPrefix.Length..];
        return new IPAddress(packed).ToString();
    }

    private static (byte[] Network, int Prefix) ParseNetwork(string network)
    {
        var address = network;
        int? prefix = null;
        if (network.Contains('/'))
        {
            var parts = network.Split('/', 2);
            address = parts[0];
            if (parts[1].Length == 0 || !parts[1].All(char.IsAsciiDigit))
                throw new ArgumentException($"Network \"{network}\" has a non-numeric CIDR prefix length");
            prefix = int.Parse(parts[1]);
        }

        if (!TryPack(address, out var packed))
            throw new ArgumentException($"Network \"{network}\" is not an IP address or CIDR block");

        var bits = packed.Length * 8;
        prefix ??= bits;
        if (prefix > bits)
            throw new ArgumentException($"Network \"{network}\" prefix length must be between 0 and {bits}");
        return (packed, prefix.Value);
    }

    private static bool InNetwork(byte[] packed, byte[] network, int prefix)
    {
        if (packed.Length != network.Length)
            return false;

        var wholeBytes = prefix >> 3;
        if (!packed.AsSpan(0, wholeBytes).SequenceEqual(network.AsSpan(0, wholeBytes)))
            return false;

        var remainingBits = prefix & 7;
        if (remainingBits == 0)
            return true;

        var mask = (0xff << (8 - remainingBits)) & 0xff;
        return (packed[wholeBytes] & mask) == (network[wholeBytes] & mask);
    }
}
