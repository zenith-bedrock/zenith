using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Zenith.NetherNet.Axolotl.Signaling.Http;

/// <summary>
/// A byte stream that never blocks: reads and writes return 0 when they would have to wait, so
/// <see cref="HttpConnection"/> can be driven from a tick loop like the reference's PHP streams.
/// </summary>
public interface IPolledStream : IDisposable
{
    /// <summary>True once the peer has closed its side and everything it sent has been read.</summary>
    bool IsEof { get; }

    /// <summary>The next unread byte without consuming it, or null when nothing has arrived.</summary>
    int? Peek();

    /// <summary>Copies available bytes into the buffer; returns 0 when none are available.</summary>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>Writes as many bytes as the stream accepts without waiting; returns how many were taken.</summary>
    int Write(ReadOnlySpan<byte> data);

    /// <summary>Starts the TLS handshake as the server; returns true once it completed, false while still running.</summary>
    bool PollHandshake(X509Certificate2 certificate);
}

/// <summary>Plain TCP over a non-blocking socket.</summary>
public sealed class PolledSocketStream : IPolledStream
{
    private readonly Socket _socket;
    private bool _eof;

    public PolledSocketStream(Socket socket)
    {
        _socket = socket;
        _socket.Blocking = false;
    }

    public bool IsEof => _eof;

    public int? Peek()
    {
        if (_socket.Available == 0)
        {
            CheckPeerGone();
            return null;
        }
        var one = new byte[1];
        try
        {
            return _socket.Receive(one, 0, 1, SocketFlags.Peek) == 1 ? one[0] : null;
        }
        catch (SocketException)
        {
            _eof = true;
            return null;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (_eof)
            return 0;
        try
        {
            if (_socket.Available == 0)
            {
                CheckPeerGone();
                return 0;
            }
            return _socket.Receive(buffer, offset, count, SocketFlags.None);
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
        {
            return 0;
        }
        catch (SocketException)
        {
            _eof = true;
            return 0;
        }
    }

    // A readable socket with no bytes available means the peer closed (or reset) the connection.
    private void CheckPeerGone()
    {
        try
        {
            if (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0)
                _eof = true;
        }
        catch (SocketException)
        {
            _eof = true;
        }
        catch (ObjectDisposedException)
        {
            _eof = true;
        }
    }

    public int Write(ReadOnlySpan<byte> data)
    {
        try
        {
            return _socket.Send(data, SocketFlags.None);
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
        {
            return 0;
        }
        catch (SocketException)
        {
            _eof = true;
            return 0;
        }
    }

    public bool PollHandshake(X509Certificate2 certificate) =>
        throw new InvalidOperationException("Plain streams do not perform a TLS handshake");

    public void Dispose()
    {
        try
        {
            _socket.Shutdown(SocketShutdown.Send);
        }
        catch (Exception)
        {
            // Already closed.
        }
        _socket.Dispose();
    }
}

/// <summary>
/// TLS over a socket. The handshake, reads and writes run as background tasks whose completion is polled;
/// the plain bytes seen before the handshake (used for sniffing) come from the underlying socket.
/// </summary>
public sealed class PolledTlsStream : IPolledStream
{
    private readonly Socket _socket;
    private readonly PolledSocketStream _sniffer;
    private SslStream? _ssl;
    private Task? _handshake;
    private Task<int>? _pendingRead;
    private readonly byte[] _readBuffer = new byte[65536];
    private int _readLength;
    private int _readOffset;
    private Task? _pendingWrite;
    private bool _eof;

    public PolledTlsStream(Socket socket)
    {
        _socket = socket;
        _sniffer = new PolledSocketStream(socket);
    }

    public bool IsEof => _ssl is null ? _sniffer.IsEof : _eof && _readOffset >= _readLength;

    public int? Peek()
    {
        if (_ssl is null)
            return _sniffer.Peek();
        PumpRead();
        return _readOffset < _readLength ? _readBuffer[_readOffset] : null;
    }

    public bool PollHandshake(X509Certificate2 certificate)
    {
        if (_handshake is null)
        {
            // The handshake is driven by a blocking socket on a worker thread; polling never waits on it.
            _socket.Blocking = true;
            _ssl = new SslStream(new NetworkStream(_socket, ownsSocket: false), leaveInnerStreamOpen: false);
            _handshake = _ssl.AuthenticateAsServerAsync(certificate, clientCertificateRequired: false,
                enabledSslProtocols: System.Security.Authentication.SslProtocols.None, checkCertificateRevocation: false);
        }

        if (!_handshake.IsCompleted)
            return false;
        if (_handshake.IsFaulted)
            throw new HttpException(HttpStatus.BadRequest, "TLS handshake failed: " + (_handshake.Exception?.GetBaseException().Message ?? "unknown error"));
        if (_handshake.IsCanceled)
            throw new HttpException(HttpStatus.BadRequest, "TLS handshake failed: cancelled");
        return true;
    }

    private void PumpRead()
    {
        if (_ssl is null)
            return;

        if (_pendingRead is { IsCompleted: true } done)
        {
            _pendingRead = null;
            if (done.IsFaulted || done.IsCanceled || done.Result == 0)
            {
                _eof = true;
            }
            else
            {
                _readOffset = 0;
                _readLength = done.Result;
            }
        }

        if (_pendingRead is null && !_eof && _readOffset >= _readLength)
        {
            _readOffset = _readLength = 0;
            _pendingRead = _ssl.ReadAsync(_readBuffer, 0, _readBuffer.Length);
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (_ssl is null)
            return _sniffer.Read(buffer, offset, count);

        PumpRead();
        if (_readOffset >= _readLength)
            return 0;

        var take = Math.Min(count, _readLength - _readOffset);
        Buffer.BlockCopy(_readBuffer, _readOffset, buffer, offset, take);
        _readOffset += take;
        return take;
    }

    public int Write(ReadOnlySpan<byte> data)
    {
        if (_ssl is null)
            return _sniffer.Write(data);

        if (_pendingWrite is { IsCompleted: false })
            return 0;
        if (_pendingWrite is { IsFaulted: true })
        {
            _eof = true;
            return 0;
        }

        _pendingWrite = _ssl.WriteAsync(data.ToArray()).AsTask();
        return data.Length;
    }

    public void Dispose()
    {
        try
        {
            // Let an in-flight response reach the client before the connection is torn down.
            _pendingWrite?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // The write already failed; nothing left to flush.
        }

        try
        {
            _ssl?.Dispose();
        }
        catch (Exception)
        {
            // Already closed.
        }
        _socket.Dispose();
    }
}
