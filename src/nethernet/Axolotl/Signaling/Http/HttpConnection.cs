using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Zenith.NetherNet.Axolotl.Negotiation;
using Zenith.NetherNet.Axolotl.Utils;
using System.Globalization;

namespace Zenith.NetherNet.Axolotl.Signaling.Http;

/// <summary>
/// One signaling client connection, advanced a step per tick: optional TLS sniffing and handshake, request head,
/// body, negotiation wait, response (port of <c>HttpConnection</c>).
/// </summary>
public sealed partial class HttpConnection
{
    public const int MaxHeadSize = 16384;
    public const int MaxBodySize = 65536;
    public const double HeadTimeout = 5.0;
    public const double BodyTimeout = 10.0;
    public const double WriteTimeout = 10.0;

    private const byte TlsHandshakeRecord = 0x16;
    private const byte TlsMajorVersion = 0x03;
    private const int ReadChunkSize = 65536;
    private const int MaxDrainOnClose = 262144;
    private static readonly byte[] ContinueResponse = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");

    [GeneratedRegex(@"[^\x20-\x7e]")]
    private static partial Regex NonPrintable();

    private readonly IPolledStream _stream;
    private readonly InternetAddress? _peerAddress;
    private readonly X509Certificate2? _certificate;
    private readonly INetherNetLogger _logger;

    private double _requestStart;
    private byte[] _input = new byte[1024];
    private int _inputLength;
    private byte[] _output = [];
    private int _outputOffset;
    private HttpRequest? _request;
    private int _bodyOffset;
    private int _contentLength;
    private INegotiation? _negotiation;

    public HttpConnection(IPolledStream stream, InternetAddress? peerAddress, X509Certificate2? certificate, INetherNetLogger logger, double now)
    {
        _stream = stream;
        _peerAddress = peerAddress;
        _certificate = certificate;
        _logger = logger;
        _requestStart = now;
        Deadline = now + HeadTimeout;
        State = certificate is not null ? HttpConnectionState.Sniffing : HttpConnectionState.ReadingHead;
    }

    public HttpConnectionState State { get; private set; }
    public double Deadline { get; private set; }
    public bool IsDone => State == HttpConnectionState.Done;
    public string PeerName => _peerAddress?.ToString() ?? "unknown";

    public void Advance(double now, JoinHandler handler)
    {
        try
        {
            Step(now, handler);
        }
        catch (HttpException e)
        {
            _logger.Debug($"{PeerName}: {e.Message}");
            Respond(new HttpResponse(e.Status), now);
        }
        catch (Exception e)
        {
            _logger.Error($"{PeerName}: unhandled {e.GetType().Name}: {e.Message}");
            Respond(new HttpResponse(HttpStatus.InternalServerError), now);
        }
    }

    private void Step(double now, JoinHandler handler)
    {
        if (State == HttpConnectionState.Sniffing && !Sniff(now))
            return;
        if (State == HttpConnectionState.TlsHandshake && !Handshake(now))
            return;
        if (State is HttpConnectionState.ReadingHead or HttpConnectionState.ReadingHeadToRedirect && !ReadHead(now, handler))
            return;

        switch (State)
        {
            case HttpConnectionState.ReadingBody:
                ReadBody(now, handler);
                break;
            case HttpConnectionState.Negotiating:
                PollNegotiation(now, handler);
                break;
            case HttpConnectionState.Writing:
                Write(now);
                break;
        }
    }

    private void TransitionTo(HttpConnectionState state, double deadline)
    {
        State = state;
        Deadline = deadline;
    }

    // With TLS configured the first byte tells a handshake (0x16) from plaintext HTTP, which is redirected to HTTPS.
    private bool Sniff(double now)
    {
        var peeked = _stream.Peek();
        if (peeked is null)
        {
            if (now > Deadline)
                throw new HttpException(HttpStatus.RequestTimeout, "Timed out waiting for a TLS handshake");
            CloseIfPeerGone();
            return false;
        }

        State = peeked == TlsHandshakeRecord ? HttpConnectionState.TlsHandshake : HttpConnectionState.ReadingHeadToRedirect;
        return true;
    }

    private bool Handshake(double now)
    {
        if (!_stream.PollHandshake(_certificate!))
        {
            if (now > Deadline)
                throw new HttpException(HttpStatus.RequestTimeout, "Timed out completing the TLS handshake");
            CloseIfPeerGone();
            return false;
        }

        _requestStart = now;
        TransitionTo(HttpConnectionState.ReadingHead, now + HeadTimeout);
        return true;
    }

    private bool ReadHead(double now, JoinHandler handler)
    {
        ReadInto(MaxHeadSize + 1);

        // A TLS ClientHello arriving on an endpoint without a certificate is a configuration mistake worth naming.
        if (State == HttpConnectionState.ReadingHead && _inputLength >= 3 && _input[0] == TlsHandshakeRecord && _input[1] == TlsMajorVersion)
            throw new HttpException(HttpStatus.BadRequest, "Client initiated a TLS handshake on a plaintext endpoint; TLS certificate is not configured");

        if (now > Deadline)
            throw new HttpException(HttpStatus.RequestTimeout,
                $"Timed out reading the request head after {_inputLength} bytes: {DescribeBytes(_input, _inputLength)}");

        var headEnd = FindHeadEnd();
        if (headEnd is null)
        {
            if (_inputLength > MaxHeadSize)
                throw new HttpException(HttpStatus.RequestHeaderFieldsTooLarge, "Request head is too large");
            CloseIfPeerGone();
            return false;
        }

        var (end, terminatorLength) = headEnd.Value;
        if (end > MaxHeadSize)
            throw new HttpException(HttpStatus.RequestHeaderFieldsTooLarge, "Request head is too large");

        _request = HttpRequest.Parse(Encoding.Latin1.GetString(_input, 0, end));
        _bodyOffset = end + terminatorLength;

        if (State == HttpConnectionState.ReadingHeadToRedirect)
        {
            Respond(new HttpResponse(HttpStatus.PermanentRedirect, headers:
                [new(HttpResponse.HeaderLocation, "https://" + (_request.GetHeader(HttpRequest.HeaderHost) ?? "") + _request.Target)]), now);
            return false;
        }

        handler.Route(_request);

        var expectsContinue = string.Equals((_request.GetHeader(HttpRequest.HeaderExpect) ?? "").Trim(), "100-continue", StringComparison.OrdinalIgnoreCase);
        _contentLength = _request.Method == HttpRequest.MethodPost ? _request.GetContentLength(MaxBodySize) : 0;
        TransitionTo(HttpConnectionState.ReadingBody, _requestStart + BodyTimeout);

        if (expectsContinue && _inputLength - _bodyOffset < _contentLength)
        {
            var written = _stream.Write(ContinueResponse);
            if (written != 0 && written != ContinueResponse.Length)
            {
                State = HttpConnectionState.Done;
                return false;
            }
        }
        return true;
    }

    private void ReadBody(double now, JoinHandler handler)
    {
        var request = _request ?? throw new InvalidOperationException("Reading a body before the request head was read");

        ReadInto(_bodyOffset + _contentLength);
        if (now > Deadline)
            throw new HttpException(HttpStatus.RequestTimeout, "Timed out reading the request body");

        if (_inputLength - _bodyOffset < _contentLength)
        {
            CloseIfPeerGone();
            return;
        }

        var body = Encoding.UTF8.GetString(_input, _bodyOffset, _contentLength);
        var result = handler.Handle(request, body, _peerAddress);
        if (result.Response is not null)
        {
            Respond(result.Response, now);
        }
        else if (result.Negotiation is not null)
        {
            _negotiation = result.Negotiation;
            TransitionTo(HttpConnectionState.Negotiating, double.PositiveInfinity);
        }
        else
        {
            State = HttpConnectionState.Done;
        }
    }

    private void PollNegotiation(double now, JoinHandler handler)
    {
        var response = _negotiation is not null ? handler.RespondTo(_negotiation) : null;
        if (response is not null)
            Respond(response, now);
    }

    private void Write(double now)
    {
        Flush();
        if (State == HttpConnectionState.Writing && now > Deadline)
        {
            _logger.Debug($"{PeerName}: timed out sending the response");
            State = HttpConnectionState.Done;
        }
    }

    private void Respond(HttpResponse response, double now)
    {
        if (State is HttpConnectionState.Writing or HttpConnectionState.Done)
            return;

        _logger.Debug($"{PeerName}: {(_request is null ? "<unparsed request>" : $"{_request.Method} {_request.GetPath()}")} -> {(int)response.Status} {response.Status.GetReasonPhrase()}");

        _output = response.Encode(omitBody: _request?.Method == HttpRequest.MethodHead);
        _outputOffset = 0;
        TransitionTo(HttpConnectionState.Writing, now + WriteTimeout);
        Flush();
    }

    private void Flush()
    {
        while (_outputOffset < _output.Length)
        {
            var written = _stream.Write(_output.AsSpan(_outputOffset));
            if (written == 0)
            {
                if (_stream.IsEof)
                    State = HttpConnectionState.Done;
                return;
            }
            _outputOffset += written;
        }
        State = HttpConnectionState.Done;
    }

    private void ReadInto(int limit)
    {
        int wanted;
        while ((wanted = limit - _inputLength) > 0)
        {
            var chunk = Math.Min(wanted, ReadChunkSize);
            if (_inputLength + chunk > _input.Length)
                Array.Resize(ref _input, Math.Max(_input.Length * 2, _inputLength + chunk));

            var read = _stream.Read(_input, _inputLength, chunk);
            if (read <= 0)
                return;
            _inputLength += read;
        }
    }

    private void CloseIfPeerGone()
    {
        if (!_stream.IsEof)
            return;
        if (_inputLength > 0)
            _logger.Debug($"{PeerName}: closed the connection after {_inputLength} bytes of a request");
        State = HttpConnectionState.Done;
    }

    public void Close()
    {
        // An unanswered negotiation would otherwise keep a half-open peer connection alive.
        if (_negotiation is not null && (_negotiation.Answer is null || _outputOffset < _output.Length))
            _negotiation.Fail("Signaling connection closed before the answer was delivered");

        State = HttpConnectionState.Done;

        // Draining unread input avoids a TCP reset that could discard the response the client has not read yet.
        var drained = 0;
        var scratch = new byte[ReadChunkSize];
        while (drained < MaxDrainOnClose)
        {
            int read;
            try
            {
                read = _stream.Read(scratch, 0, scratch.Length);
            }
            catch (Exception)
            {
                break;
            }
            if (read <= 0)
                break;
            drained += read;
        }
        _stream.Dispose();
    }

    private (int End, int TerminatorLength)? FindHeadEnd()
    {
        var span = _input.AsSpan(0, _inputLength);
        var crlf = span.IndexOf("\r\n\r\n"u8);
        var lf = span.IndexOf("\n\n"u8);
        if (crlf >= 0 && (lf < 0 || crlf < lf))
            return (crlf, 4);
        if (lf >= 0)
            return (lf, 2);
        return null;
    }

    private static string DescribeBytes(byte[] data, int length, int limit = 96)
    {
        var sampleLength = Math.Min(length, limit);
        var hex = string.Join(' ', data.Take(Math.Min(sampleLength, 16)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        var text = NonPrintable().Replace(Encoding.Latin1.GetString(data, 0, sampleLength), ".");
        return $"hex[{hex}] text[{text}]" + (length > limit ? "..." : "");
    }
}
