using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Zenith.NetherNet;

public interface INetherNetOfferHandler
{
    Task<string?> CreateAnswerAsync(string networkId, string offerSdp, CancellationToken cancellationToken);
}

public sealed record NetherNetServerInfo(
    string Name,
    int Protocol,
    string Version,
    string Level,
    int Players,
    int MaxPlayers,
    int GameType);

/// <summary>HTTP one-round-trip signaling endpoint for NetherNet full-ICE negotiation.</summary>
public sealed class NetherNetSignalingServer : IAsyncDisposable
{
    private const string SdpContentType = "application/sdp";
    private const string JsonContentType = "application/json; charset=utf-8";
    private readonly HttpListener _listener = new();
    private readonly INetherNetOfferHandler _offerHandler;
    private readonly Func<NetherNetServerInfo> _serverInfo;
    private readonly int _maximumOfferSize;
    private readonly string _basePath;
    private readonly string _listenerPrefix;
    private readonly Action<Exception>? _onRequestError;
    private readonly object _lifecycleGate = new();
    private readonly object _requestGate = new();
    private readonly HashSet<Task> _requests = [];
    private CancellationTokenSource? _shutdown;
    private Task? _acceptTask;
    private Task? _disposeTask;
    private bool _disposed;

    public NetherNetSignalingServer(
        Uri listenUri,
        NetherNetServerInfo serverInfo,
        INetherNetOfferHandler offerHandler,
        int maximumOfferSize)
        : this(listenUri, listenUri, () => serverInfo, offerHandler, maximumOfferSize)
    {
    }

    public NetherNetSignalingServer(
        Uri listenUri,
        Func<NetherNetServerInfo> serverInfo,
        INetherNetOfferHandler offerHandler,
        int maximumOfferSize)
        : this(listenUri, listenUri, serverInfo, offerHandler, maximumOfferSize)
    {
    }

    public NetherNetSignalingServer(
        Uri listenUri,
        Uri joinUri,
        Func<NetherNetServerInfo> serverInfo,
        INetherNetOfferHandler offerHandler,
        int maximumOfferSize)
        : this(listenUri?.ToString() ?? throw new ArgumentNullException(nameof(listenUri)),
            joinUri, serverInfo, offerHandler, maximumOfferSize)
    {
    }

    public NetherNetSignalingServer(
        string listenUrl,
        Uri joinUri,
        Func<NetherNetServerInfo> serverInfo,
        INetherNetOfferHandler offerHandler,
        int maximumOfferSize,
        Action<Exception>? onRequestError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listenUrl);
        ArgumentNullException.ThrowIfNull(joinUri);
        ArgumentNullException.ThrowIfNull(serverInfo);
        ArgumentNullException.ThrowIfNull(offerHandler);
        if (maximumOfferSize is < 1 or > int.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(maximumOfferSize));

        _offerHandler = offerHandler;
        _serverInfo = serverInfo;
        _maximumOfferSize = maximumOfferSize;
        _onRequestError = onRequestError;
        _basePath = joinUri.AbsolutePath.TrimEnd('/') + "/";
        _listenerPrefix = CreateListenerPrefix(listenUrl, _basePath);
        _listener.Prefixes.Add(_listenerPrefix);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
                return Task.FromException(new ObjectDisposedException(nameof(NetherNetSignalingServer)));
            if (_acceptTask is not null)
                return Task.FromException(new InvalidOperationException("NetherNet signaling is already started."));

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException exception) when (OperatingSystem.IsWindows() && exception.NativeErrorCode == 5)
            {
                var account = $"{Environment.UserDomainName}\\{Environment.UserName}";
                throw new InvalidOperationException(
                    $"Windows denied access to the NetherNet signaling URL prefix '{_listenerPrefix}'. " +
                    "Reserve it once in a PowerShell terminal running as administrator for the account that runs the server:" + Environment.NewLine +
                    $"netsh http add urlacl url=\"{_listenerPrefix}\" user=\"{account}\" listen=yes delegate=no" + Environment.NewLine +
                    "Then restart Zenith under that account.", exception);
            }
            _shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return _acceptTask = AcceptLoopAsync(_shutdown.Token);
        }
    }

    public void StopAccepting()
    {
        CancellationTokenSource? shutdown;
        lock (_lifecycleGate)
        {
            if (_disposed)
                return;
            shutdown = _shutdown;
        }
        if (shutdown is null)
            return;
        shutdown.Cancel();
        if (_listener.IsListening)
            _listener.Stop();
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<bool> completion;
        CancellationTokenSource? shutdown;
        Task? acceptTask;
        lock (_lifecycleGate)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);
            _disposed = true;
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            shutdown = _shutdown;
            acceptTask = _acceptTask;
        }

        _ = DisposeCoreAsync(shutdown, acceptTask, completion);
        return new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(
        CancellationTokenSource? shutdown,
        Task? acceptTask,
        TaskCompletionSource<bool> completion)
    {
        Exception? failure = null;
        try
        {
            try { shutdown?.Cancel(); }
            catch (Exception exception) { failure = exception; }
            try
            {
                if (_listener.IsListening)
                    _listener.Stop();
            }
            catch (Exception exception) { failure ??= exception; }
            if (acceptTask is not null)
            {
                try { await acceptTask.ConfigureAwait(false); }
                catch (OperationCanceledException) when (shutdown?.IsCancellationRequested == true) { }
                catch (HttpListenerException) when (shutdown?.IsCancellationRequested == true) { }
                catch (Exception exception) { failure ??= exception; }
            }
            Task[] requests;
            lock (_requestGate)
                requests = _requests.ToArray();
            try { await Task.WhenAll(requests).ConfigureAwait(false); }
            catch (OperationCanceledException) when (shutdown?.IsCancellationRequested == true) { }
            catch (Exception exception) { failure ??= exception; }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try { shutdown?.Dispose(); }
            catch (Exception exception) { failure ??= exception; }
            try { _listener.Close(); }
            catch (Exception exception) { failure ??= exception; }
        }

        if (failure is null)
            completion.TrySetResult(true);
        else
            completion.TrySetException(failure);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var request = HandleRequestAsync(context, cancellationToken);
            lock (_requestGate)
                _requests.Add(request);
            _ = request.ContinueWith(
                completed =>
                {
                    lock (_requestGate)
                        _requests.Remove(completed);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals(_basePath + "v1/join", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, 200, _serverInfo(), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                TryGetNetworkId(path, out var networkId))
            {
                if (!IsSdpContentType(context.Request.ContentType))
                {
                    await WriteTextAsync(context.Response, 415, "Content-Type must be application/sdp.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (context.Request.ContentLength64 == 0)
                {
                    await WriteTextAsync(context.Response, 400, "The SDP offer must not be empty.", cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (context.Request.ContentLength64 > _maximumOfferSize)
                {
                    await WriteTextAsync(context.Response, 413, "The SDP offer exceeds the configured limit.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                string offer;
                try
                {
                    offer = await ReadBoundedAsync(context.Request.InputStream, _maximumOfferSize, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    await WriteTextAsync(context.Response, 413, "The SDP offer exceeds the configured limit.", cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (DecoderFallbackException)
                {
                    await WriteTextAsync(context.Response, 400, "The SDP offer must be valid UTF-8.", cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (offer.Length == 0)
                {
                    await WriteTextAsync(context.Response, 400, "The SDP offer must not be empty.", cancellationToken).ConfigureAwait(false);
                    return;
                }
                var answer = await _offerHandler.CreateAnswerAsync(networkId, offer, cancellationToken).ConfigureAwait(false);
                if (answer is null)
                {
                    await WriteTextAsync(context.Response, 503, "NetherNet negotiation is unavailable.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await WriteSdpAsync(context.Response, 200, answer, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteTextAsync(context.Response, 404, "NetherNet endpoint not found.", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _onRequestError?.Invoke(exception);
            await WriteTextAsync(context.Response, 500, "NetherNet signaling failed.", CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
            {
                // A canceled/in-flight response can be closed before its declared content length
                // is written. The listener is already stopping, so its response stream is closed.
            }
        }
    }

    private bool TryGetNetworkId(string path, out string networkId)
    {
        networkId = string.Empty;
        var prefix = _basePath + "v1/join/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var encoded = path[prefix.Length..];
        if (encoded.Length == 0 || encoded.Contains('/'))
            return false;
        networkId = Uri.UnescapeDataString(encoded);
        return networkId.Length > 0;
    }

    private static string CreateListenerPrefix(string listenUrl, string basePath)
    {
        const string wildcardPrefix = "http://+:";
        var isWildcard = listenUrl.StartsWith(wildcardPrefix, StringComparison.OrdinalIgnoreCase);
        var parseableUrl = isWildcard
            ? "http://127.0.0.1:" + listenUrl[wildcardPrefix.Length..]
            : listenUrl;
        if (!Uri.TryCreate(parseableUrl, UriKind.Absolute, out var listenUri) ||
            listenUri.Scheme != "http" ||
            !string.IsNullOrEmpty(listenUri.UserInfo) ||
            !string.IsNullOrEmpty(listenUri.Query) ||
            !string.IsNullOrEmpty(listenUri.Fragment))
            throw new ArgumentException("NetherNet signaling requires an absolute HTTP listener URL without credentials, query, or fragment.", nameof(listenUrl));

        return isWildcard
            ? $"http://+:{listenUri.Port}{basePath}"
            : new UriBuilder(listenUri) { Path = basePath }.Uri.ToString();
    }

    private static bool IsSdpContentType(string? contentType)
    {
        return contentType is not null &&
               MediaTypeHeaderValue.TryParse(contentType, out var parsed) &&
               string.Equals(parsed.MediaType, SdpContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (content.Length + read > maximumBytes)
                throw new InvalidOperationException("The SDP offer exceeds the configured limit.");
            content.Write(buffer, 0, read);
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(content.GetBuffer(), 0, (int)content.Length);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object body, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        response.ContentType = JsonContentType;
        await WriteAsync(response, status, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteSdpAsync(HttpListenerResponse response, int status, string body, CancellationToken cancellationToken)
    {
        response.ContentType = SdpContentType;
        return WriteAsync(response, status, Encoding.UTF8.GetBytes(body), cancellationToken);
    }

    private static Task WriteTextAsync(HttpListenerResponse response, int status, string body, CancellationToken cancellationToken)
    {
        response.ContentType = "text/plain; charset=utf-8";
        return WriteAsync(response, status, Encoding.UTF8.GetBytes(body), cancellationToken);
    }

    private static async Task WriteAsync(HttpListenerResponse response, int status, byte[] bytes, CancellationToken cancellationToken)
    {
        response.StatusCode = status;
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
