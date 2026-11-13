using System.Net;
using System.Runtime.CompilerServices;
using System.Net.Sockets;

namespace Zenith.NetherNet.Axolotl.Utils;

public class SocketException : NetherNetException
{
    public int ErrorNumber { get; }

    public SocketException(string message, int errorNumber = 0, Exception? inner = null) : base(message, inner)
    {
        ErrorNumber = errorNumber;
    }

    public static SocketException FromErrno(string what, System.Net.Sockets.SocketException e) =>
        new($"{what}: {e.Message}", e.ErrorCode, e);
}

/// <summary>
/// Minimal logging surface used by the library (the reference uses PocketMine's <c>Logger</c>). This package stays free of other
/// Zenith packages, so it carries its own copy of the allocation-free Debug mechanism that <c>Zenith.Logging.ILogger</c> has.
/// </summary>
public interface INetherNetLogger
{
    /// <summary>False when Debug output is discarded; <c>Debug($"...")</c> then builds and evaluates nothing (see <see cref="NetherNetDebugHandler"/>).</summary>
    bool IsDebugEnabled => true;

    void Debug(string message);

    /// <summary>Chosen by the compiler for interpolated strings: no formatting, allocation or argument evaluation while Debug is off.</summary>
    void Debug([InterpolatedStringHandlerArgument("")] ref NetherNetDebugHandler message)
    {
        if (message.IsEnabled)
            Debug(message.ToStringAndClear());
    }

    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

public sealed class NullNetherNetLogger : INetherNetLogger
{
    public static readonly NullNetherNetLogger Instance = new();

    public bool IsDebugEnabled => false;
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
}

/// <summary>A normalized IP address, port and IP version (port of <c>InternetAddress</c>).</summary>
public sealed class InternetAddress
{
    public string Ip { get; }
    public int Port { get; }
    public int Version { get; }

    public InternetAddress(string ip, int port, int version)
    {
        if (port < 0 || port > 65535)
            throw new ArgumentException($"Port must be between 0 and 65535, got {port}");
        if (version != 4 && version != 6)
            throw new ArgumentException($"IP version must be 4 or 6, got {version}");
        Port = port;
        Version = version;
        Ip = NormalizeIp(ip) ?? ip;
    }

    /// <summary>Strict IP validation: <c>IPAddress.TryParse</c> alone would accept shorthand such as "1".</summary>
    public static bool TryParseIp(string ip, out IPAddress address)
    {
        address = IPAddress.None;
        if (ip.Length == 0)
            return false;
        if (ip.Contains(':'))
        {
            if (ip.Contains('%') || !IPAddress.TryParse(ip, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
                return false;
            address = v6;
            return true;
        }
        var parts = ip.Split('.');
        if (parts.Length != 4 || parts.Any(p => p.Length is < 1 or > 3 || !p.All(char.IsAsciiDigit) || int.Parse(p) > 255))
            return false;
        if (!IPAddress.TryParse(ip, out var v4))
            return false;
        address = v4;
        return true;
    }

    public static string? NormalizeIp(string ip)
    {
        if (ip.StartsWith('[') && ip.EndsWith(']'))
            ip = ip[1..^1];
        return TryParseIp(ip, out var address) ? address.ToString() : null;
    }

    public static InternetAddress? Parse(string address)
    {
        var separator = address.LastIndexOf(':');
        if (separator < 0)
            return null;
        var ip = address[..separator];
        var port = address[(separator + 1)..];
        if (ip.StartsWith('[') && ip.EndsWith(']'))
            ip = ip[1..^1];
        if (port.Length == 0 || !port.All(char.IsAsciiDigit) || !int.TryParse(port, out var portNumber) || portNumber > 65535
            || !TryParseIp(ip, out var parsed))
            return null;
        return new InternetAddress(ip, portNumber, parsed.AddressFamily == AddressFamily.InterNetworkV6 ? 6 : 4);
    }

    public bool Equals(InternetAddress other) => Ip == other.Ip && Port == other.Port && Version == other.Version;

    public override string ToString() => (Version == 6 ? $"[{Ip}]" : Ip) + ":" + Port;
}
