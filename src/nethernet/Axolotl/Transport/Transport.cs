using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Protocol.Framing;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Transport;

public class TransportException : NetherNetException
{
    public TransportException(string message) : base(message) { }
    public TransportException(string message, Exception? inner) : base(message, inner) { }
}

public enum TransportState
{
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Closed,
}

public static class TransportStateExtensions
{
    public static bool IsBroken(this TransportState state) => state is TransportState.Failed or TransportState.Closed;
}

/// <summary>One WebRTC data channel exposed as a message queue (port of <c>MessageChannel</c>).</summary>
public interface IMessageChannel
{
    string Label { get; }

    /// <summary>The reliability this channel implements, or null when its label or settings are not NetherNet's.</summary>
    Reliability? Reliability { get; }

    bool IsOpen { get; }
    bool IsClosed { get; }
    int MaxMessageSize { get; }
    void Send(ReadOnlySpan<byte> message);
    byte[]? Receive();
    long BufferedAmount { get; }
    long AvailableAmount { get; }
    int QueuedMessageCount { get; }
    void Close();
}

public sealed record ChannelPair(IMessageChannel Reliable, IMessageChannel Unreliable)
{
    public IMessageChannel Get(Reliability reliability) => reliability switch
    {
        Protocol.Reliability.Reliable => Reliable,
        Protocol.Reliability.Unreliable => Unreliable,
        _ => throw new ArgumentOutOfRangeException(nameof(reliability)),
    };

    public IMessageChannel[] All() => [Reliable, Unreliable];
}

/// <summary>One WebRTC peer connection driven by polling (port of <c>PeerTransport</c>).</summary>
public interface IPeerTransport
{
    void SetRemoteOffer(string sdp);
    string? GetLocalDescription();
    bool IsGatheringComplete { get; }
    void AddRemoteCandidate(string candidate);
    IReadOnlyList<string> PollLocalCandidates();
    IReadOnlyList<IMessageChannel> PollChannels();
    TransportState State { get; }
    bool HasLostConnectivity { get; }
    InternetAddress? RemoteAddress { get; }
    int? RoundTripTime { get; }
    long BytesSent { get; }
    long BytesReceived { get; }
    void Close();
}

public interface IPeerTransportFactory
{
    IPeerTransport Create(ConnectionBudgetConfiguration budget);
}

/// <summary>Per-connection resource limits (port of <c>ConnectionBudgetConfiguration</c>).</summary>
public sealed class ConnectionBudgetConfiguration
{
    public const int DefaultMaxMessageSize = Segmenter.MaxSegmentPayloadSize + Segmenter.HeaderSize;
    public const int DefaultMaxPayloadSize = 256 * 9999;
    public const int DefaultMaxReceiveQueueSize = 256 * 10000;
    public const int DefaultMaxReceiveQueueMessages = 512;
    public const int DefaultMaxSendQueueSize = 4194304;
    public const int DefaultMaxPendingDataChannels = 4;

    public int MaxMessageSize { get; }
    public int MaxPayloadSize { get; }
    public int MaxReceiveQueueSize { get; }
    public int MaxReceiveQueueMessages { get; }
    public int MaxSendQueueSize { get; }
    public int MaxPendingDataChannels { get; }

    public ConnectionBudgetConfiguration(
        int maxMessageSize = DefaultMaxMessageSize,
        int maxPayloadSize = DefaultMaxPayloadSize,
        int maxReceiveQueueSize = DefaultMaxReceiveQueueSize,
        int maxReceiveQueueMessages = DefaultMaxReceiveQueueMessages,
        int maxSendQueueSize = DefaultMaxSendQueueSize,
        int maxPendingDataChannels = DefaultMaxPendingDataChannels)
    {
        if (maxMessageSize < 2 || maxMessageSize > DefaultMaxMessageSize)
            throw new ArgumentException($"Maximum message size must be between 2 and {DefaultMaxMessageSize}, got {maxMessageSize}");
        if (maxPayloadSize < maxMessageSize - Segmenter.HeaderSize || maxPayloadSize > Segmenter.MaxPayloadSize)
            throw new ArgumentException($"Maximum payload size must be between {maxMessageSize - Segmenter.HeaderSize} and {Segmenter.MaxPayloadSize}, got {maxPayloadSize}");
        if (maxReceiveQueueSize < maxMessageSize)
            throw new ArgumentException($"Maximum receive queue size must be at least the size of one message ({maxMessageSize} bytes), got {maxReceiveQueueSize}");
        if (maxReceiveQueueMessages < 1)
            throw new ArgumentException($"Maximum receive queue message count must be positive, got {maxReceiveQueueMessages}");
        if (maxSendQueueSize < maxMessageSize)
            throw new ArgumentException($"Maximum send queue size must be at least the size of one message ({maxMessageSize} bytes), got {maxSendQueueSize}");
        if (maxPendingDataChannels < 2)
            throw new ArgumentException($"Maximum pending data channels must be at least 2, got {maxPendingDataChannels}");

        MaxMessageSize = maxMessageSize;
        MaxPayloadSize = maxPayloadSize;
        MaxReceiveQueueSize = maxReceiveQueueSize;
        MaxReceiveQueueMessages = maxReceiveQueueMessages;
        MaxSendQueueSize = maxSendQueueSize;
        MaxPendingDataChannels = maxPendingDataChannels;
    }
}
