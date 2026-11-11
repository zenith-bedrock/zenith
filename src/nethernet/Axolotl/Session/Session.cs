using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Protocol.Framing;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Session;

public class SessionException : NetherNetException
{
    public SessionException(string message) : base(message) { }
    public SessionException(string message, Exception? inner) : base(message, inner) { }
}

public enum SessionState
{
    Connected,
    DisconnectPending,
    Disconnected,
}

public sealed record ReceivedMessage(Reliability Reliability, byte[] Payload);

/// <summary>One connected peer: framing, queue limits and the disconnect handshake (port of <c>Session</c>).</summary>
public sealed class Session
{
    public const double DisconnectTimeout = 3.0;

    private static readonly Reliability[] AllReliabilities = [Reliability.Reliable, Reliability.Unreliable];

    private readonly IPeerTransport _transport;
    private readonly ChannelPair _channels;
    private readonly Segmenter _segmenter;
    private readonly ConnectionBudgetConfiguration _budget;
    private readonly Dictionary<Reliability, Assembler> _assemblers = new();
    private readonly Dictionary<Reliability, long> _queuedBytes = new();
    private readonly Dictionary<Reliability, Queue<(int ReceiptId, long Watermark)>> _pendingReceipts = new();
    private int _pendingReceiptCount;

    private SessionState _state = SessionState.Connected;
    private DisconnectReason? _disconnectReason;
    private long _reportedBytesSent;
    private long _reportedBytesReceived;
    private int? _reportedPing;
    private double? _disconnectDeadline;
    private bool _awaitPeerClose = true;

    public Session(int id, IPeerTransport transport, ChannelPair channels, InternetAddress address, string? clientAddress,
        Segmenter segmenter, ConnectionBudgetConfiguration budget)
    {
        Id = id;
        _transport = transport;
        _channels = channels;
        RemoteAddress = address;
        ClientAddress = clientAddress;
        _segmenter = segmenter;
        _budget = budget;

        foreach (var reliability in AllReliabilities)
        {
            _assemblers[reliability] = new Assembler(reliability.IsFragmentationSupported(), budget.MaxPayloadSize);
            _queuedBytes[reliability] = 0;
            _pendingReceipts[reliability] = new Queue<(int, long)>();
        }
    }

    public int Id { get; }
    public InternetAddress RemoteAddress { get; }
    public string? ClientAddress { get; }
    public bool IsConnected => _state == SessionState.Connected;
    public bool IsFullyDisconnected => _state == SessionState.Disconnected;
    public DisconnectReason? DisconnectReason => _disconnectReason;

    /// <summary>Sends one payload, fragmenting it on the reliable channel; a receipt id is reported once it is delivered.</summary>
    public void Send(ReadOnlySpan<byte> payload, Reliability reliability = Reliability.Reliable, int? receiptId = null)
    {
        if (_state == SessionState.DisconnectPending)
            return;
        RequireOpen();

        IReadOnlyList<byte[]> segments;
        try
        {
            segments = _segmenter.Segment(payload);
        }
        catch (FramingException e)
        {
            throw new ArgumentException(e.Message, e);
        }

        if (segments.Count > 1 && !reliability.IsFragmentationSupported())
            throw new ArgumentException($"Payload requires {segments.Count} segments, but {reliability.GetChannelLabel()} does not support fragmentation");

        try
        {
            var channel = _channels.Get(reliability);
            foreach (var segment in segments)
            {
                channel.Send(segment);
                _queuedBytes[reliability] += segment.Length;
            }
        }
        catch (TransportException e)
        {
            ForciblyDisconnect(Protocol.DisconnectReason.SendFailed);
            throw new SessionException($"Failed to send on channel {reliability.GetChannelLabel()}: {e.Message}", e);
        }

        if (receiptId is not null)
        {
            _pendingReceipts[reliability].Enqueue((receiptId.Value, _queuedBytes[reliability]));
            _pendingReceiptCount++;
        }
    }

    public IReadOnlyList<int> TakeAcknowledgedReceipts()
    {
        if (_state == SessionState.Disconnected || _pendingReceiptCount == 0)
            return [];

        var acknowledged = new List<int>();
        foreach (var reliability in AllReliabilities)
        {
            var pending = _pendingReceipts[reliability];
            if (pending.Count == 0)
                continue;

            var delivered = _queuedBytes[reliability] - _channels.Get(reliability).BufferedAmount;
            while (pending.Count > 0)
            {
                var (receiptId, watermark) = pending.Peek();
                if (watermark > delivered)
                    break;
                pending.Dequeue();
                _pendingReceiptCount--;
                acknowledged.Add(receiptId);
            }
        }
        return acknowledged;
    }

    public ReceivedMessage? Receive()
    {
        RequireOpen();
        foreach (var reliability in AllReliabilities)
        {
            var channel = _channels.Get(reliability);
            var assembler = _assemblers[reliability];
            try
            {
                byte[]? segment;
                while ((segment = channel.Receive()) is not null)
                {
                    var payload = assembler.Accept(segment);
                    if (payload is not null)
                        return new ReceivedMessage(reliability, payload);
                }
            }
            catch (Exception e) when (e is FramingException or TransportException)
            {
                ForciblyDisconnect(Protocol.DisconnectReason.BadData);
                throw new SessionException($"Bad data on channel {reliability.GetChannelLabel()}: {e.Message}", e);
            }
        }
        return null;
    }

    /// <summary>Bytes (sent, received) since the previous call.</summary>
    public (long Sent, long Received) TakeBandwidth()
    {
        var sent = _transport.BytesSent;
        var received = _transport.BytesReceived;
        var diff = (sent - _reportedBytesSent, received - _reportedBytesReceived);
        _reportedBytesSent = sent;
        _reportedBytesReceived = received;
        return diff;
    }

    public int? TakePingChange()
    {
        var ping = _transport.RoundTripTime;
        if (ping is null || ping == _reportedPing)
            return null;
        return _reportedPing = ping;
    }

    public void Update(double now)
    {
        if (_state == SessionState.Disconnected)
            return;

        var transportState = _transport.State;
        if (transportState == TransportState.Failed)
        {
            ForciblyDisconnect(Protocol.DisconnectReason.ConnectionFailed);
            return;
        }

        if (_state == SessionState.DisconnectPending)
        {
            _disconnectDeadline ??= now + DisconnectTimeout;
            var completed = _awaitPeerClose ? HasClosedChannel() : BufferedBytes() == 0;
            var channelFailed = !DiscardIncoming();
            if (completed || channelFailed || transportState != TransportState.Connected || now >= _disconnectDeadline)
                ForciblyDisconnect(_disconnectReason ?? Protocol.DisconnectReason.ServerDisconnect);
            return;
        }

        long queuedBytes = 0;
        var queuedMessages = 0;
        foreach (var channel in _channels.All())
        {
            queuedBytes += channel.AvailableAmount;
            queuedMessages += channel.QueuedMessageCount;
        }

        if (queuedBytes > _budget.MaxReceiveQueueSize)
        {
            ForciblyDisconnect(Protocol.DisconnectReason.ReceiveQueueTooManyBytes);
            return;
        }
        if (queuedMessages > _budget.MaxReceiveQueueMessages)
        {
            ForciblyDisconnect(Protocol.DisconnectReason.ReceiveQueueTooManyMessages);
            return;
        }
        if (BufferedBytes() > _budget.MaxSendQueueSize)
        {
            ForciblyDisconnect(Protocol.DisconnectReason.SendQueueTooManyBytes);
            return;
        }
        if (transportState == TransportState.Closed || HasClosedChannel())
            ForciblyDisconnect(GetTransportDisconnectReason());
    }

    private bool DiscardIncoming()
    {
        foreach (var channel in _channels.All())
        {
            try
            {
                while (channel.Receive() is not null)
                {
                }
            }
            catch (TransportException)
            {
                return false;
            }
        }
        return true;
    }

    private bool HasClosedChannel() => _channels.All().Any(c => c.IsClosed);

    private Protocol.DisconnectReason GetTransportDisconnectReason() =>
        _transport.HasLostConnectivity ? Protocol.DisconnectReason.ConnectionFailed : Protocol.DisconnectReason.PeerDisconnect;

    /// <summary>Starts a graceful disconnect that completes when the peer closes (or buffers drain) or the timeout passes.</summary>
    public void InitiateDisconnect(DisconnectReason reason, bool awaitPeerClose = true)
    {
        if (_state != SessionState.Connected)
            return;
        _state = SessionState.DisconnectPending;
        _disconnectReason = reason;
        _awaitPeerClose = awaitPeerClose;
    }

    private long BufferedBytes() => _channels.All().Sum(c => c.BufferedAmount);

    public void ForciblyDisconnect(DisconnectReason reason)
    {
        if (_state == SessionState.Disconnected)
            return;
        _state = SessionState.Disconnected;
        _disconnectReason = reason;
        foreach (var channel in _channels.All())
            channel.Close();
        _transport.Close();
    }

    private void RequireOpen()
    {
        if (_state == SessionState.Disconnected)
            throw new SessionException("Session is closed" + (_disconnectReason is { } reason ? ": " + reason.GetMessage() : ""));
        if (HasClosedChannel())
        {
            ForciblyDisconnect(GetTransportDisconnectReason());
            throw new SessionException("Data channel of the session is closed");
        }
    }
}
