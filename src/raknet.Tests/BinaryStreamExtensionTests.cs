using System.Net;
using System.Net.Sockets;
using Xunit;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Stream;

namespace raknet.Tests;

public class BinaryStreamExtensionTests
{
    [Fact]
    public void IPv4_endpoint_round_trips()
    {
        var original = new IPEndPoint(IPAddress.Parse("192.168.1.42"), 19132);
        var writer = new BinaryStream();
        writer.WriteIPEndPoint(original);

        var reader = new BinaryStream(writer.Buffer, writer.Length);
        var result = reader.ReadIPEndPoint();

        Assert.Equal(original.Address, result.Address);
        Assert.Equal(original.Port, result.Port);
        writer.Dispose();
        reader.Dispose();
    }

    /// <summary>
    /// Regression: the IPv6 read path used to do IPAddress.Parse(span.ToString()), which never
    /// produces a parseable address string (Span<byte>.ToString() yields the type name) — this
    /// always threw for a real IPv6 client. Fixed to new IPAddress(ReadOnlySpan<byte>).
    /// </summary>
    [Fact]
    public void IPv6_endpoint_round_trips()
    {
        var original = new IPEndPoint(IPAddress.Parse("2001:db8::1"), 19133);
        Assert.Equal(AddressFamily.InterNetworkV6, original.AddressFamily);
        var writer = new BinaryStream();
        writer.WriteIPEndPoint(original);

        var reader = new BinaryStream(writer.Buffer, writer.Length);
        var result = reader.ReadIPEndPoint();

        Assert.Equal(original.Address, result.Address);
        Assert.Equal(original.Port, result.Port);
        writer.Dispose();
        reader.Dispose();
    }
}
