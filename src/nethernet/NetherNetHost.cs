using System.Collections.Concurrent;
using System.Threading.Channels;
using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Events;
using DataChannelDotnet.Impl;

namespace Zenith.NetherNet;

public sealed class NetherNetConnection : IAsyncDisposable
{
    private readonly RtcPeerConnection _peer;
    private IRtcDataChannel? _reliable;
    private IRtcDataChannel? _unreliable;
    private readonly NetherNetFrameCodec.ReliableReassembler _reassembler;
    private readonly object _channelGate = new();
    private readonly TaskCompletionSource _channelsOpen = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private byte _openMask;
    private readonly Channel<Outbound> _outbound = Channel.CreateBounded<Outbound>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly Task _sendWorker;
    private readonly int _maximumMessageSize;
    private readonly int _maximumFragmentSize;
    private int _closed;
    private string? _closeReason;

    internal NetherNetConnection(
        string networkId,
        NetherNetClientIdentity? clientIdentity,
        RtcPeerConnection peer,
        int maximumMessageSize,
        int maximumFragmentSize)
    {
        NetworkId = networkId;
        ClientIdentity = clientIdentity;
        _peer = peer;
        _maximumMessageSize = maximumMessageSize;
        _maximumFragmentSize = maximumFragmentSize;
        _reassembler = new NetherNetFrameCodec.ReliableReassembler(maximumMessageSize);
        _sendWorker = Task.Run(SendLoopAsync);

        _peer.OnConnectionStateChange += OnPeerStateChanged;
    }

    public string NetworkId { get; }
    public NetherNetClientIdentity? ClientIdentity { get; }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public bool AreDataChannelsOpen => Volatile.Read(ref _openMask) == 3;
    public string? CloseReason => Volatile.Read(ref _closeReason);
    public string WebRtcConnectionState => _peer.ConnectionState.ToString();
    public string WebRtcIceState => _peer.IceState.ToString();
    public event Action<NetherNetConnection, byte[]>? GameBatchReceived;
    public event Action<NetherNetConnection, string>? Closed;

    internal void AddDataChannel(IRtcDataChannel channel)
    {
        // Native channel metadata may already be disposed when close/error callbacks run.
        var label = channel.Label;
        string? failure = null;
        lock (_channelGate)
        {
            if (label == "ReliableDataChannel")
            {
                if (_reliable is not null)
                    failure = "Duplicate reliable NetherNet channel.";
                else
                {
                    _reliable = channel;
                    channel.OnBinaryReceivedSafe += OnReliableMessage;
                }
            }
            else if (label == "UnreliableDataChannel")
            {
                if (_unreliable is not null)
                    failure = "Duplicate unreliable NetherNet channel.";
                else
                {
                    _unreliable = channel;
                    channel.OnBinaryReceivedSafe += OnUnreliableMessage;
                }
            }
            else
                failure = $"Unexpected NetherNet data channel '{label}'.";

            if (failure is null)
            {
                channel.OnOpen += OnDataChannelOpen;
                channel.OnClose += _ => OnDataChannelClosed(label);
                channel.OnError += (_, error) => OnDataChannelError(label, error);
            }
        }

        if (failure is not null)
            Close(failure);
        else if (channel.IsOpen)
            OnDataChannelOpen(channel);
    }

    public bool SendReliable(ReadOnlySpan<byte> batch)
    {
        if (IsClosed || batch.Length == 0 || batch.Length > _maximumMessageSize)
            return false;
        try
        {
            return _outbound.Writer.TryWrite(new Outbound(
                NetherNetFrameCodec.EncodeReliable(batch, _maximumFragmentSize, _maximumMessageSize), false));
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public bool SendUnreliable(ReadOnlySpan<byte> batch)
    {
        if (IsClosed || batch.Length == 0 || batch.Length > _maximumFragmentSize - 1)
            return false;
        return _outbound.Writer.TryWrite(new Outbound(
            [NetherNetFrameCodec.EncodeUnreliable(batch, _maximumMessageSize)], true));
    }

    public void FlushOutgoing()
    {
        if (IsClosed)
            return;
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_outbound.Writer.TryWrite(new Outbound([], false, flushed)))
            throw new InvalidOperationException("NetherNet outgoing queue is full while flushing.");
        if (!flushed.Task.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Timed out draining the NetherNet data channel queue.");
        flushed.Task.GetAwaiter().GetResult();
    }

    public void Disconnect(string reason = "") => Close(reason);

    public async ValueTask DisposeAsync()
    {
        Close("disposed");
        try { await _sendWorker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private void OnReliableMessage(IRtcDataChannel channel, RtcBinaryReceivedEventSafe message)
    {
        byte[]? batch;
        try
        {
            if (message.Data.Length > _maximumFragmentSize + 1)
                throw new FormatException("Reliable NetherNet frame exceeds the configured fragment limit.");
            if (!_reassembler.TryAppend(message.Data, out batch))
                return;
        }
        catch
        {
            Close("invalid reliable frame");
            return;
        }
        if (batch is not null)
            GameBatchReceived?.Invoke(this, batch);
    }

    private void OnUnreliableMessage(IRtcDataChannel channel, RtcBinaryReceivedEventSafe message)
    {
        if (!NetherNetFrameCodec.TryDecodeUnreliable(message.Data, _maximumMessageSize, out var batch))
        {
            Close("invalid unreliable frame");
            return;
        }
        GameBatchReceived?.Invoke(this, batch.ToArray());
    }

    private void OnPeerStateChanged(IRtcPeerConnection peer, rtcState state)
    {
        if (state is rtcState.RTC_FAILED or rtcState.RTC_CLOSED)
            Close($"WebRTC {state}");
    }

    private void OnDataChannelOpen(IRtcDataChannel channel)
    {
        lock (_channelGate)
        {
            if (ReferenceEquals(channel, _reliable))
                _openMask |= 1;
            else if (ReferenceEquals(channel, _unreliable))
                _openMask |= 2;
            if (_openMask == 3)
                _channelsOpen.TrySetResult();
        }
    }

    private void OnDataChannelClosed(string label) => Close($"Data channel '{label}' closed.");

    private void OnDataChannelError(string label, string? error) =>
        Close($"Data channel '{label}' failed: {error ?? "unknown error"}");

    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (var work in _outbound.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (work.Flushed is not null)
                {
                    work.Flushed.TrySetResult();
                    continue;
                }

                await _channelsOpen.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                var channel = work.Unreliable ? _unreliable : _reliable;
                if (channel is null)
                    throw new InvalidOperationException("NetherNet data channel disappeared before sending.");
                foreach (var frame in work.Frames)
                {
                    if (IsClosed || !channel.IsOpen)
                        throw new InvalidOperationException("NetherNet data channel is not open.");
                    channel.Send(frame);
                }
            }
        }
        catch (Exception exception)
        {
            Close($"WebRTC send failed: {exception.Message}");
        }
    }

    private void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        Volatile.Write(ref _closeReason, reason);
        _outbound.Writer.TryComplete();
        _channelsOpen.TrySetException(new IOException($"NetherNet closed before both data channels opened: {reason}"));
        _peer.Dispose();
        Closed?.Invoke(this, reason);
    }

    private sealed record Outbound(IReadOnlyList<byte[]> Frames, bool Unreliable, TaskCompletionSource? Flushed = null);
}

/// <summary>HTTP signaling plus WebRTC peers for the independent NetherNet transport.</summary>
public sealed class NetherNetHost : INetherNetOfferHandler, IAsyncDisposable
{
    private readonly NetherNetServerIdentity _identity;
    private readonly Func<string, CancellationToken, Task<bool>>? _validateClientToken;
    private readonly bool _allowAnonymous;
    private readonly bool _allowUnverifiedToken;
    private readonly int _maximumMessageSize;
    private readonly int _maximumFragmentSize;
    private readonly string? _iceBindAddress;
    private readonly string[] _iceServers;
    private readonly ushort _icePortRangeBegin;
    private readonly ushort _icePortRangeEnd;
    private readonly SemaphoreSlim _connectionCapacity;
    private readonly ConcurrentDictionary<string, NetherNetConnection> _connections = new(StringComparer.Ordinal);
    private readonly NetherNetSignalingServer _signaling;

    public NetherNetHost(
        string listenUrl,
        Uri joinUri,
        Func<NetherNetServerInfo> serverInfo,
        NetherNetServerIdentity identity,
        Func<string, CancellationToken, Task<bool>>? validateClientToken,
        bool allowAnonymous,
        bool allowUnverifiedToken,
        int maximumMessageSize,
        int maximumFragmentSize,
        int maximumConnections,
        int maximumOfferSize = 131_072,
        string? iceBindAddress = null,
        ushort icePortRangeBegin = 0,
        ushort icePortRangeEnd = 0,
        IReadOnlyCollection<string>? iceServers = null,
        Action<Exception>? onSignalingError = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _validateClientToken = validateClientToken;
        _allowAnonymous = allowAnonymous;
        _allowUnverifiedToken = allowUnverifiedToken;
        _maximumMessageSize = maximumMessageSize;
        _maximumFragmentSize = maximumFragmentSize;
        _iceBindAddress = iceBindAddress;
        _iceServers = iceServers?.ToArray() ?? [];
        _icePortRangeBegin = icePortRangeBegin;
        _icePortRangeEnd = icePortRangeEnd;
        _connectionCapacity = new SemaphoreSlim(maximumConnections, maximumConnections);
        _signaling = new NetherNetSignalingServer(
            listenUrl, joinUri, serverInfo, this, maximumOfferSize, onSignalingError);
    }

    public event Action<NetherNetConnection>? ConnectionOpened;

    public Task StartAsync(CancellationToken cancellationToken = default) => _signaling.StartAsync(cancellationToken);

    public void StopAccepting() => _signaling.StopAccepting();

    public void DisconnectAll(string reason)
    {
        foreach (var connection in _connections.Values)
            connection.Disconnect(reason);
    }

    public async Task<string?> CreateAnswerAsync(string networkId, string offerSdp, CancellationToken cancellationToken)
    {
        if (!_connectionCapacity.Wait(0))
            return null;
        var capacityTransferredToConnection = false;
        RtcPeerConnection? peer = null;
        NetherNetConnection? connection = null;
        try
        {
            var clientIdentity = await NetherNetSdpIdentity.ValidateOfferAsync(
                offerSdp, _validateClientToken, _allowAnonymous, _allowUnverifiedToken, cancellationToken).ConfigureAwait(false);

            peer = new RtcPeerConnection(new RtcPeerConfiguration
            {
                IceServers = _iceServers,
                BindAddress = _iceBindAddress,
                MaxMessageSize = Math.Min(_maximumFragmentSize + 1, 262_144),
                DisableAutoNegotiation = true,
                EnableIceUdpMux = false,
                PortRangeBegin = _icePortRangeBegin,
                PortRangeEnd = _icePortRangeEnd
            });
            var channelGate = new object();
            var pendingChannels = new List<IRtcDataChannel>();
            NetherNetConnection? activeConnection = null;
            peer.OnDataChannel += (_, channel) =>
            {
                NetherNetConnection? active;
                lock (channelGate)
                {
                    active = activeConnection;
                    if (active is null)
                        pendingChannels.Add(channel);
                }
                active?.AddDataChannel(channel);
            };

            peer.SetRemoteDescription(new RtcDescription { Type = RtcDescriptionType.Offer, Sdp = NetherNetSdpIdentity.RemoveIdentity(offerSdp) });
            connection = new NetherNetConnection(networkId, clientIdentity, peer,
                _maximumMessageSize, _maximumFragmentSize);
            if (!_connections.TryAdd(networkId, connection))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException("A NetherNet connection already uses this NetworkID.");
            }
            capacityTransferredToConnection = true;
            connection.Closed += OnConnectionClosed;
            IRtcDataChannel[] arrived;
            lock (channelGate)
            {
                activeConnection = connection;
                arrived = pendingChannels.ToArray();
                pendingChannels.Clear();
            }
            foreach (var channel in arrived)
                connection.AddDataChannel(channel);
            if (connection.IsClosed)
                throw new FormatException("The offer contains invalid NetherNet data channels.");

            ConnectionOpened?.Invoke(connection);
            var gatheringComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnGatheringState(IRtcPeerConnection _, rtcGatheringState state)
            {
                if (state == rtcGatheringState.RTC_GATHERING_COMPLETE)
                    gatheringComplete.TrySetResult();
            }

            peer.OnGatheringStateChange += OnGatheringState;
            try
            {
                peer.SetLocalDescription(RtcDescriptionType.Answer);
                if (peer.GatheringState != rtcGatheringState.RTC_GATHERING_COMPLETE)
                    await gatheringComplete.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                peer.OnGatheringStateChange -= OnGatheringState;
            }

            var answerSdp = peer.LocalDescription;
            if (string.IsNullOrWhiteSpace(answerSdp))
                throw new InvalidOperationException("The WebRTC library did not produce a local SDP answer.");
            return _identity.AddAssertion(answerSdp);
        }
        catch
        {
            if (capacityTransferredToConnection)
                connection?.Disconnect("SDP answer creation failed.");
            else
            {
                if (connection is not null)
                    await connection.DisposeAsync().ConfigureAwait(false);
                else
                    peer?.Dispose();
                _connectionCapacity.Release();
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopAccepting();
        DisconnectAll("Server shutdown.");
        await _signaling.DisposeAsync().ConfigureAwait(false);
        foreach (var connection in _connections.Values)
            await connection.DisposeAsync().ConfigureAwait(false);
        _identity.Dispose();
        _connectionCapacity.Dispose();
    }

    private void OnConnectionClosed(NetherNetConnection connection, string reason)
    {
        if (_connections.TryGetValue(connection.NetworkId, out var current) && ReferenceEquals(current, connection))
        {
            if (_connections.TryRemove(connection.NetworkId, out _))
                _connectionCapacity.Release();
        }
    }
}
