using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Zenith.NetherNet.Axolotl.Signaling.Http;

public enum HttpStatus
{
    Ok = 200,
    PermanentRedirect = 308,
    BadRequest = 400,
    NotFound = 404,
    MethodNotAllowed = 405,
    RequestTimeout = 408,
    LengthRequired = 411,
    ContentTooLarge = 413,
    UriTooLong = 414,
    UnsupportedMediaType = 415,
    RequestHeaderFieldsTooLarge = 431,
    InternalServerError = 500,
    NotImplemented = 501,
}

public static class HttpStatusExtensions
{
    public static string GetReasonPhrase(this HttpStatus status) => status switch
    {
        HttpStatus.Ok => "OK",
        HttpStatus.PermanentRedirect => "Permanent Redirect",
        HttpStatus.BadRequest => "Bad Request",
        HttpStatus.NotFound => "Not Found",
        HttpStatus.MethodNotAllowed => "Method Not Allowed",
        HttpStatus.RequestTimeout => "Request Timeout",
        HttpStatus.LengthRequired => "Length Required",
        HttpStatus.ContentTooLarge => "Content Too Large",
        HttpStatus.UriTooLong => "URI Too Long",
        HttpStatus.UnsupportedMediaType => "Unsupported Media Type",
        HttpStatus.RequestHeaderFieldsTooLarge => "Request Header Fields Too Large",
        HttpStatus.InternalServerError => "Internal Server Error",
        HttpStatus.NotImplemented => "Not Implemented",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

public class HttpException(HttpStatus status, string message) : SignalingException(message)
{
    public HttpStatus Status { get; } = status;
}

public enum HttpConnectionState
{
    Sniffing,
    TlsHandshake,
    ReadingHead,
    ReadingHeadToRedirect,
    ReadingBody,
    Negotiating,
    Writing,
    Done,
}

public static class HttpConnectionStateExtensions
{
    public static bool IsAwaitingRequest(this HttpConnectionState state) => state is
        HttpConnectionState.Sniffing or HttpConnectionState.TlsHandshake or HttpConnectionState.ReadingHead
        or HttpConnectionState.ReadingHeadToRedirect or HttpConnectionState.ReadingBody;
}

/// <summary>A strictly parsed HTTP/1.1 request head (port of <c>HttpRequest</c>).</summary>
public sealed partial class HttpRequest
{
    public const string MethodGet = "GET";
    public const string MethodHead = "HEAD";
    public const string MethodPost = "POST";
    public const string HeaderContentLength = "content-length";
    public const string HeaderTransferEncoding = "transfer-encoding";
    public const string HeaderContentType = "content-type";
    public const string HeaderConnection = "connection";
    public const string HeaderHost = "host";
    public const string HeaderExpect = "expect";

    private static readonly HashSet<string> DecisiveHeaders =
        [HeaderContentLength, HeaderTransferEncoding, HeaderContentType, HeaderConnection, HeaderHost];

    [GeneratedRegex(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+\z")]
    public static partial Regex TokenPattern();

    [GeneratedRegex(@"^([A-Z]+) (/[^\x00-\x20\x7f]*) (HTTP/1\.1)\z")]
    private static partial Regex RequestLinePattern();

    [GeneratedRegex(@"[\x00-\x08\x0a-\x1f\x7f]")]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"^\d+\z")]
    private static partial Regex DigitsOnly();

    public string Method { get; }
    public string Target { get; }
    public string Version { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }

    private HttpRequest(string method, string target, string version, Dictionary<string, string> headers)
    {
        Method = method;
        Target = target;
        Version = version;
        Headers = headers;
    }

    /// <summary>Parses a head (request line and headers, without the blank line); throws <see cref="HttpException"/> when malformed.</summary>
    public static HttpRequest Parse(string head)
    {
        var lines = head.Replace("\r\n", "\n").Split('\n');
        var requestLine = lines.Length > 0 ? lines[0] : "";
        var match = RequestLinePattern().Match(requestLine);
        if (!match.Success)
            throw new HttpException(HttpStatus.BadRequest, "Malformed request line");

        var headers = new Dictionary<string, string>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;
            if (line[0] is ' ' or '\t')
                throw new HttpException(HttpStatus.BadRequest, "Obsolete line folding is not accepted");

            var colon = line.IndexOf(':');
            if (colon < 0)
                throw new HttpException(HttpStatus.BadRequest, "Malformed header line");

            var name = line[..colon];
            if (name.Length == 0)
                throw new HttpException(HttpStatus.BadRequest, "Empty header name");
            if (name.TrimEnd() != name)
                throw new HttpException(HttpStatus.BadRequest, $"Whitespace is not allowed before the colon in \"{name}\"");
            if (!TokenPattern().IsMatch(name))
                throw new HttpException(HttpStatus.BadRequest, "Header name is not a valid token");

            name = name.ToLowerInvariant();
            if (headers.ContainsKey(name) && DecisiveHeaders.Contains(name))
                throw new HttpException(HttpStatus.BadRequest, $"Duplicate {name} header");

            var value = line[(colon + 1)..].Trim();
            if (ControlCharacters().IsMatch(value))
                throw new HttpException(HttpStatus.BadRequest, $"Header \"{name}\" contains a control character");

            headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;
        }

        if (headers.ContainsKey(HeaderTransferEncoding))
            throw new HttpException(HttpStatus.NotImplemented, "Transfer-Encoding is not supported");
        if (!headers.ContainsKey(HeaderHost))
            throw new HttpException(HttpStatus.BadRequest, "Host header is required");

        return new HttpRequest(match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value, match.Groups[3].Value, headers);
    }

    public string? GetHeader(string name) => Headers.GetValueOrDefault(name.ToLowerInvariant());

    /// <summary>The request target without its query string or fragment.</summary>
    public string GetPath()
    {
        var target = Target;
        foreach (var separator in new[] { '?', '#' })
        {
            var position = target.IndexOf(separator);
            if (position >= 0)
                target = target[..position];
        }
        return target;
    }

    public int GetContentLength(int limit)
    {
        var value = GetHeader(HeaderContentLength)
            ?? throw new HttpException(HttpStatus.LengthRequired, "Content-Length is required");
        if (!DigitsOnly().IsMatch(value))
            throw new HttpException(HttpStatus.BadRequest, "Content-Length must be a non-negative integer");
        if (!long.TryParse(value, out var length) || length > limit)
            throw new HttpException(HttpStatus.ContentTooLarge, $"Body length ({value} bytes) exceeds limit of {limit} bytes");
        return (int)length;
    }

    public bool HasContentType(string expected)
    {
        var value = GetHeader(HeaderContentType);
        if (value is null)
            return false;
        var semicolon = value.IndexOf(';');
        var mediaType = semicolon < 0 ? value : value[..semicolon];
        return string.Equals(mediaType.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HttpResponse(
    HttpStatus status,
    string body = "",
    string? contentType = null,
    IReadOnlyList<KeyValuePair<string, string>>? headers = null)
{
    public const string HeaderContentType = "Content-Type";
    public const string HeaderContentLength = "Content-Length";
    public const string HeaderConnection = "Connection";
    public const string HeaderLocation = "Location";

    public HttpStatus Status { get; } = status;
    public string Body { get; } = body;
    public string? ContentType { get; } = contentType;

    /// <summary>Serializes the response; <paramref name="omitBody"/> keeps the headers (and length) for HEAD.</summary>
    public byte[] Encode(bool omitBody = false)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(Body);
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append((int)Status).Append(' ').Append(Status.GetReasonPhrase()).Append("\r\n");
        if (ContentType is not null)
            head.Append(HeaderContentType).Append(": ").Append(ContentType).Append("\r\n");
        foreach (var (name, value) in headers ?? [])
            head.Append(name).Append(": ").Append(value).Append("\r\n");
        head.Append(HeaderContentLength).Append(": ").Append(bodyBytes.Length).Append("\r\n");
        head.Append(HeaderConnection).Append(": close\r\n\r\n");

        var headBytes = Encoding.ASCII.GetBytes(head.ToString());
        return omitBody ? headBytes : [.. headBytes, .. bodyBytes];
    }
}

/// <summary>Certificate settings for HTTPS signaling (port of <c>TlsConfiguration</c>).</summary>
public sealed record TlsConfiguration(string CertificateFile, string? PrivateKeyFile = null, string? Passphrase = null)
{
    public void CheckReadable()
    {
        foreach (var path in new[] { CertificateFile, PrivateKeyFile })
            if (path is not null && !File.Exists(path))
                throw new SignalingException($"TLS is configured with {path}, which cannot be read");
    }

    public X509Certificate2 LoadCertificate()
    {
        try
        {
            var certificate = PrivateKeyFile is not null
                ? (Passphrase is not null
                    ? X509Certificate2.CreateFromEncryptedPemFile(CertificateFile, Passphrase, PrivateKeyFile)
                    : X509Certificate2.CreateFromPemFile(CertificateFile, PrivateKeyFile))
                : (Passphrase is not null
                    ? X509CertificateLoader.LoadPkcs12FromFile(CertificateFile, Passphrase)
                    : X509Certificate2.CreateFromPemFile(CertificateFile));

            // Windows SslStream needs a key with a persisted provider; re-importing through PKCS#12 provides one.
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            throw new SignalingException("TLS certificate could not be loaded: " + e.Message, e);
        }
    }
}
