using System.Collections.Concurrent;
using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Transport.WebRtc;

/// <summary>A STUN or TURN server for ICE (port of the reference <c>NetherNetIceServer</c>).</summary>
public sealed record IceServer(bool IsTurn, string Host, int Port, string Username = "", string Password = "")
{
    /// <summary>libdatachannel URL form: <c>stun:host:port</c> or <c>turn:user:pass@host:port</c>.</summary>
    public string ToUrl()
    {
        var host = Host.Contains(':') && !Host.StartsWith('[') ? $"[{Host}]" : Host;
        if (!IsTurn)
            return $"stun:{host}:{Port}";
        var credentials = Username.Length > 0
            ? $"{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@"
            : "";
        return $"turn:{credentials}{host}:{Port}";
    }
}

/// <summary>Process-wide SCTP tuning (port of <c>SctpConfiguration</c>).</summary>
/// <remarks>libdatachannel's SCTP settings struct has no heartbeat interval, so that value is validated but not applied.</remarks>
public sealed class SctpConfiguration
{
    public int? HeartbeatInterval { get; }
    public int? MaxRetransmitAttempts { get; }
    public int? MinRetransmitTimeout { get; }
    public int? MaxRetransmitTimeout { get; }
    public int? InitialRetransmitTimeout { get; }

    public SctpConfiguration(
        int? heartbeatInterval = null,
        int? maxRetransmitAttempts = null,
        int? minRetransmitTimeout = null,
        int? maxRetransmitTimeout = null,
        int? initialRetransmitTimeout = null)
    {
        foreach (var (name, value) in new (string, int?)[]
                 {
                     ("Heartbeat interval", heartbeatInterval),
                     ("Maximum retransmit attempts", maxRetransmitAttempts),
                     ("Minimum retransmit timeout", minRetransmitTimeout),
                     ("Maximum retransmit timeout", maxRetransmitTimeout),
                     ("Initial retransmit timeout", initialRetransmitTimeout),
                 })
        {
            if (value is < 1)
                throw new ArgumentException($"{name} must be positive, got {value}");
        }
        if (minRetransmitTimeout > maxRetransmitTimeout)
            throw new ArgumentException($"Minimum retransmit timeout ({minRetransmitTimeout}) cannot be greater than the maximum ({maxRetransmitTimeout})");

        HeartbeatInterval = heartbeatInterval;
        MaxRetransmitAttempts = maxRetransmitAttempts;
        MinRetransmitTimeout = minRetransmitTimeout;
        MaxRetransmitTimeout = maxRetransmitTimeout;
        InitialRetransmitTimeout = initialRetransmitTimeout;
    }

    public void ApplyGlobally()
    {
        // Zero leaves libdatachannel's default in place.
        var settings = new rtcSctpSettings
        {
            maxRetransmitAttempts = MaxRetransmitAttempts ?? 0,
            minRetransmitTimeoutMs = MinRetransmitTimeout ?? 0,
            maxRetransmitTimeoutMs = MaxRetransmitTimeout ?? 0,
            initialRetransmitTimeoutMs = InitialRetransmitTimeout ?? 0,
        };
        RtcTools.SetSctpSettings(settings);
    }
}

/// <summary>A data channel with the received messages queued for polling (port of <c>WebRtcMessageChannel</c>).</summary>
/// <remarks>
/// DataChannelDotnet delivers messages through events and does not expose the channel's ordering or retransmit
/// settings, so <see cref="Reliability"/> is derived from the label alone.
/// </remarks>
public sealed class WebRtcMessageChannel : IMessageChannel
{
    private readonly IRtcDataChannel _channel;
    private readonly int _maxMessageSize;
    private readonly ConcurrentQueue<byte[]> _received = new();
    private long _queuedBytes;
    private int _queuedMessages;
    private int _closed;

    public WebRtcMessageChannel(IRtcDataChannel channel, int maxMessageSize)
    {
        _channel = channel;
        _maxMessageSize = maxMessageSize;
        channel.OnBinaryReceivedSafe += OnBinary;
        channel.OnClose += _ => Volatile.Write(ref _closed, 1);
    }

    private void OnBinary(IRtcDataChannel _, DataChannelDotnet.Events.RtcBinaryReceivedEventSafe message)
    {
        var copy = message.Data.ToArray();
        _received.Enqueue(copy);
        Interlocked.Add(ref _queuedBytes, copy.Length);
        Interlocked.Increment(ref _queuedMessages);
    }

    public string Label => _channel.Label;

    public Reliability? Reliability => ReliabilityExtensions.FromChannelLabel(Label);

    public bool IsOpen => Volatile.Read(ref _closed) == 0 && _channel.IsOpen;

    public bool IsClosed => Volatile.Read(ref _closed) != 0 || _channel.IsClosed;

    public int MaxMessageSize => _maxMessageSize;

    public void Send(ReadOnlySpan<byte> message)
    {
        try
        {
            _channel.Send(message);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new TransportException(e.Message, e);
        }
    }

    public byte[]? Receive()
    {
        if (!_received.TryDequeue(out var message))
            return null;
        Interlocked.Add(ref _queuedBytes, -message.Length);
        Interlocked.Decrement(ref _queuedMessages);
        return message;
    }

    // The native library does not report its send buffer.
    public long BufferedAmount => 0;

    public long AvailableAmount => Interlocked.Read(ref _queuedBytes);

    public int QueuedMessageCount => Volatile.Read(ref _queuedMessages);

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        try
        {
            (_channel as IDisposable)?.Dispose();
        }
        catch (Exception)
        {
            // Closing an already-broken channel is not an error.
        }
    }
}

/// <summary>A libdatachannel peer connection used as a server-side answerer (port of <c>WebRtcPeerTransport</c>).</summary>
public sealed class WebRtcPeerTransport : IPeerTransport
{
    private readonly RtcPeerConnection _peer;
    private readonly int _maxMessageSize;
    private readonly ConcurrentQueue<IRtcDataChannel> _channels = new();
    private readonly ConcurrentQueue<string> _localCandidates = new();
    private int _gatheringComplete;
    private int _failed;
    private bool _closed;
    private bool _lostConnectivity;
    private InternetAddress? _remoteAddress;

    public WebRtcPeerTransport(RtcPeerConnection peer, int maxMessageSize)
    {
        _peer = peer;
        _maxMessageSize = maxMessageSize;
        // Handlers are attached before any description is set: channels can arrive immediately after the offer.
        peer.OnDataChannel += (_, channel) => _channels.Enqueue(channel);
        peer.OnCandidateSafe += (_, candidate) => _localCandidates.Enqueue(candidate.Content);
        peer.OnGatheringStateChange += (_, state) =>
        {
            if (state == rtcGatheringState.RTC_GATHERING_COMPLETE)
                Volatile.Write(ref _gatheringComplete, 1);
        };
        peer.OnConnectionStateChange += (_, state) =>
        {
            if (state == rtcState.RTC_FAILED)
                Volatile.Write(ref _failed, 1);
        };
    }

    public void SetRemoteOffer(string sdp)
    {
        try
        {
            _peer.SetRemoteDescription(new RtcDescription { Type = RtcDescriptionType.Offer, Sdp = sdp });
            // Auto-negotiation is disabled so the answer is created only after the data-channel handler is attached.
            _peer.SetLocalDescription(RtcDescriptionType.Answer);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new TransportException(e.Message, e);
        }
    }

    public string? GetLocalDescription()
    {
        try
        {
            var description = _peer.LocalDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new TransportException(e.Message, e);
        }
    }

    public bool IsGatheringComplete =>
        Volatile.Read(ref _gatheringComplete) != 0 || SafeGet(() => _peer.GatheringState == rtcGatheringState.RTC_GATHERING_COMPLETE, false);

    public void AddRemoteCandidate(string candidate)
    {
        try
        {
            if (candidate.StartsWith("a=", StringComparison.Ordinal))
                candidate = candidate[2..];
            _peer.AddRemoteCandidate(new RtcCandidate { Content = candidate, Mid = "0" });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new TransportException(e.Message, e);
        }
    }

    public IReadOnlyList<string> PollLocalCandidates()
    {
        var result = new List<string>();
        while (_localCandidates.TryDequeue(out var candidate))
            result.Add(candidate);
        return result;
    }

    public IReadOnlyList<IMessageChannel> PollChannels()
    {
        var result = new List<IMessageChannel>();
        while (_channels.TryDequeue(out var channel))
            result.Add(new WebRtcMessageChannel(channel, _maxMessageSize));
        return result;
    }

    public TransportState State
    {
        get
        {
            if (_closed)
                return TransportState.Closed;
            return SafeGet(() => _peer.ConnectionState switch
            {
                rtcState.RTC_NEW or rtcState.RTC_CONNECTING => TransportState.Connecting,
                rtcState.RTC_CONNECTED => TransportState.Connected,
                rtcState.RTC_DISCONNECTED => TransportState.Disconnected,
                rtcState.RTC_FAILED => TransportState.Failed,
                _ => TransportState.Closed,
            }, TransportState.Closed);
        }
    }

    public bool HasLostConnectivity => _closed ? _lostConnectivity : Volatile.Read(ref _failed) != 0;

    public InternetAddress? RemoteAddress
    {
        get
        {
            if (!_closed)
            {
                var address = SafeGet(() => _peer.RemoteAddress, null);
                if (!string.IsNullOrEmpty(address))
                    _remoteAddress = InternetAddress.Parse(address) ?? _remoteAddress;
            }
            return _remoteAddress;
        }
    }

    // The native library does not report round-trip time or byte counters.
    public int? RoundTripTime => null;

    public long BytesSent => 0;

    public long BytesReceived => 0;

    public void Close()
    {
        if (_closed)
            return;
        _lostConnectivity = HasLostConnectivity;
        _ = RemoteAddress;
        _closed = true;
        try
        {
            _peer.Dispose();
        }
        catch (Exception)
        {
            // The peer may already be torn down.
        }
    }

    private static T SafeGet<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return fallback;
        }
    }
}

/// <summary>Creates peer connections with the operator's ICE settings (port of <c>WebRtcTransportFactory</c>).</summary>
public sealed class WebRtcTransportFactory : IPeerTransportFactory
{
    public const int DefaultMtu = 1200;
    public const int MinMtu = 620;
    public const int MaxMtu = 4144;

    private readonly IReadOnlyList<IceServer> _iceServers;
    private readonly string? _bindAddress;
    private readonly int? _portRangeBegin;
    private readonly int? _portRangeEnd;
    private readonly bool _iceUdpMuxEnabled;
    private readonly int _mtu;

    public WebRtcTransportFactory(
        IReadOnlyList<IceServer>? iceServers = null,
        string? bindAddress = null,
        int? portRangeBegin = null,
        int? portRangeEnd = null,
        bool iceUdpMuxEnabled = false,
        int mtu = DefaultMtu)
    {
        iceServers ??= [];
        if ((portRangeBegin is null) != (portRangeEnd is null))
            throw new ArgumentException("Both port range begin and end must be specified together, or both must be null");
        if (portRangeBegin is < 1 or > 65535)
            throw new ArgumentException($"Port range start must be between 1 and 65535, got {portRangeBegin}");
        if (portRangeEnd is < 1 or > 65535)
            throw new ArgumentException($"Port range end must be between 1 and 65535, got {portRangeEnd}");
        if (portRangeBegin > portRangeEnd)
            throw new ArgumentException($"Port range start ({portRangeBegin}) cannot be greater than end ({portRangeEnd})");
        if (iceUdpMuxEnabled && iceServers.Any(s => s.IsTurn))
            throw new ArgumentException("TURN servers cannot be used with ICE UDP mux");
        if (mtu < MinMtu || mtu > MaxMtu)
            throw new ArgumentException($"MTU must be between {MinMtu} and {MaxMtu} bytes, got {mtu}");

        _iceServers = iceServers;
        _bindAddress = bindAddress;
        _portRangeBegin = portRangeBegin;
        _portRangeEnd = portRangeEnd;
        _iceUdpMuxEnabled = iceUdpMuxEnabled;
        _mtu = mtu;
    }

    public IPeerTransport Create(ConnectionBudgetConfiguration budget)
    {
        var configuration = new RtcPeerConfiguration
        {
            IceServers = _iceServers.Select(s => s.ToUrl()).ToList(),
            BindAddress = _bindAddress,
            MaxMessageSize = budget.MaxMessageSize,
            DisableAutoNegotiation = true,
            EnableIceTcp = false,
            EnableIceUdpMux = _iceUdpMuxEnabled,
            Mtu = _mtu,
            PortRangeBegin = (ushort)(_portRangeBegin ?? 0),
            PortRangeEnd = (ushort)(_portRangeEnd ?? 0),
        };

        try
        {
            return new WebRtcPeerTransport(new RtcPeerConnection(configuration), budget.MaxMessageSize);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new TransportException(e.Message, e);
        }
    }
}
