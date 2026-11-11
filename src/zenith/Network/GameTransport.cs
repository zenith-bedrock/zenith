namespace Zenith.Network;

/// <summary>Delivery urgency a Bedrock session asks of its transport; each transport maps it to its own queueing.</summary>
internal enum GameTransportPriority
{
    Normal,
    Immediate
}

/// <summary>Why a transport connection ended, in transport-neutral terms.</summary>
internal enum GameTransportCloseReason
{
    ClientDisconnect,
    ServerDisconnect,
    Timeout
}

/// <summary>
/// What a transport proved about a peer before any Bedrock packet flowed (e.g. NetherNet's signaling-time
/// GameServerToken). Transports with no such step expose <see cref="IGameTransportConnection.TransportIdentity"/> = null.
/// </summary>
/// <param name="GameServerToken">The token the peer presented while connecting.</param>
/// <param name="Authenticated">Whether the transport verified the token against its issuer.</param>
internal sealed record TransportIdentity(string GameServerToken, bool Authenticated);

/// <summary>Cumulative wire counters of one transport (all zero when a transport cannot measure them).</summary>
internal readonly record struct TransportStatistics(
    int Connections, long SentDatagrams, long SentBytes, long ReceivedDatagrams, long ReceivedBytes)
{
    public static TransportStatistics operator +(TransportStatistics a, TransportStatistics b) => new(
        a.Connections + b.Connections,
        a.SentDatagrams + b.SentDatagrams,
        a.SentBytes + b.SentBytes,
        a.ReceivedDatagrams + b.ReceivedDatagrams,
        a.ReceivedBytes + b.ReceivedBytes);
}

/// <summary>One peer as seen by a Bedrock protocol session: everything it may ask of the wire, and nothing transport-specific.</summary>
internal interface IGameTransportConnection
{
    /// <summary>Human-readable peer label for logs.</summary>
    string EndPoint { get; }
    bool IsClosed { get; }

    /// <summary>Set once the login handshake proves a game identity; transports may use it to prefer evicting anonymous peers.</summary>
    bool HasGameIdentity { get; set; }

    /// <summary>
    /// Non-null when the transport authenticated the peer before the Bedrock login. A session whose
    /// <see cref="RequiresTransportIdentityBinding"/> is true must reject logins that do not match it.
    /// </summary>
    TransportIdentity? TransportIdentity { get; }
    bool RequiresTransportIdentityBinding { get; }

    /// <summary>True when game batches carry the leading 0xFE RakNet game identifier.</summary>
    bool IncludesGamePacketIdentifier { get; }

    void SendGameBatch(ReadOnlyMemory<byte> batch, GameTransportPriority priority, byte orderChannel);
    void SendUnreliableGameBatch(ReadOnlyMemory<byte> batch);
    void FlushOutgoing();
    void Disconnect();
}

/// <summary>
/// The side of the server a transport talks to. Transports push connection lifecycle and inbound batches here;
/// they know nothing about Bedrock sessions, handlers or gameplay.
/// </summary>
internal interface IGameTransportHost
{
    /// <summary>Offers a freshly connected peer; false means the server is not accepting and the transport must drop it.</summary>
    bool TryOpen(IGameTransportConnection connection);

    /// <summary>Delivers one inbound game batch. The host takes ownership of (and disposes) the stream.</summary>
    bool Receive(IGameTransportConnection connection, ref Zenith.Binary.BinaryStream batch);

    void Close(IGameTransportConnection connection, GameTransportCloseReason reason);
}

/// <summary>A way for clients to reach the server (RakNet over UDP, NetherNet over WebRTC, …).</summary>
internal interface IGameTransport
{
    string Name { get; }

    /// <summary>Starts listening and completes when the transport stops; faults if it fails unexpectedly.</summary>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>Refuses new peers while existing ones keep running (first step of a graceful shutdown).</summary>
    void StopAccepting();

    /// <summary>Closes the transport and waits for it to drain. Idempotent.</summary>
    Task ShutdownAsync();

    TransportStatistics Statistics { get; }
}
