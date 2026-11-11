using System.Net;
using System.Net.Sockets;

namespace Zenith.NetherNet.Axolotl.Utils;

/// <summary>A non-blocking UDP socket (port of <c>DatagramSocket</c>).</summary>
public sealed class DatagramSocket : IDisposable
{
    private const int MaxDatagramSize = 65535;
    private readonly Socket _socket;
    private readonly byte[] _buffer = new byte[MaxDatagramSize];
    private bool _closed;

    public InternetAddress BindAddress { get; }

    public DatagramSocket(InternetAddress address, bool broadcast = false)
    {
        var ipv6 = address.Version == 6;
        try
        {
            _socket = new Socket(ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            throw SocketException.FromErrno("Could not create a UDP socket", e);
        }

        try
        {
            if (broadcast)
                _socket.EnableBroadcast = true;
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            if (ipv6)
                _socket.DualMode = false;
            _socket.Bind(new IPEndPoint(IPAddress.Parse(address.Ip), address.Port));
            _socket.Blocking = false;
            DisableConnectionReset(_socket);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            _socket.Dispose();
            throw SocketException.FromErrno($"Could not bind to {address}", e);
        }

        BindAddress = _socket.LocalEndPoint is IPEndPoint local
            ? new InternetAddress(local.Address.ToString(), local.Port, address.Version)
            : address;
    }

    // Windows reports an ICMP "port unreachable" from an earlier send as an error on the next receive.
    private static void DisableConnectionReset(Socket socket)
    {
        if (OperatingSystem.IsWindows())
        {
            const int sioUdpConnReset = -1744830452;
            socket.IOControl(sioUdpConnReset, [0], null);
        }
    }

    /// <summary>The next datagram and its sender, or null when none is waiting.</summary>
    public (byte[] Datagram, InternetAddress From)? Receive()
    {
        while (true)
        {
            try
            {
                EndPoint remote = _socket.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Any, 0)
                    : new IPEndPoint(IPAddress.Any, 0);
                var length = _socket.ReceiveFrom(_buffer, ref remote);
                var endpoint = (IPEndPoint)remote;
                return (_buffer.AsSpan(0, length).ToArray(),
                    new InternetAddress(endpoint.Address.ToString(), endpoint.Port, BindAddress.Version));
            }
            catch (System.Net.Sockets.SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                return null;
            }
            catch (System.Net.Sockets.SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                // A previous send bounced; keep reading.
            }
            catch (System.Net.Sockets.SocketException e)
            {
                throw SocketException.FromErrno("Read failed", e);
            }
        }
    }

    public void Send(ReadOnlySpan<byte> datagram, InternetAddress destination)
    {
        try
        {
            _socket.SendTo(datagram, SocketFlags.None, new IPEndPoint(IPAddress.Parse(destination.Ip), destination.Port));
        }
        catch (System.Net.Sockets.SocketException e)
        {
            throw SocketException.FromErrno($"Could not send to {destination}", e);
        }
    }

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _socket.Dispose();
    }
}
