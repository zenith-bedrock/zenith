using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.NetherNet;

namespace Zenith.Session;

internal enum GameTransportPriority
{
    Normal,
    Immediate
}

/// <summary>Transport operations consumed by one Bedrock protocol session.</summary>
internal interface IGameTransportConnection
{
    string EndPoint { get; }
    bool IsClosed { get; }
    bool HasGameIdentity { get; set; }
    bool RequiresGameServerTokenBinding { get; }
    NetherNetClientIdentity? ClientIdentity { get; }
    bool IncludesGamePacketIdentifier { get; }
    void SendGameBatch(ReadOnlyMemory<byte> batch, GameTransportPriority priority, byte orderChannel);
    void SendUnreliableGameBatch(ReadOnlyMemory<byte> batch);
    void FlushOutgoing();
    void Disconnect();
}

internal sealed class RakNetGameTransportConnection(RakNetSession session) : IGameTransportConnection
{
    public RakNetSession Session { get; } = session;
    public string EndPoint => Session.EndPoint.ToString();
    public bool IsClosed => Session.IsClosed;
    public bool HasGameIdentity { get => Session.HasGameIdentity; set => Session.HasGameIdentity = value; }
    public bool RequiresGameServerTokenBinding => false;
    public NetherNetClientIdentity? ClientIdentity => null;
    public bool IncludesGamePacketIdentifier => true;

    public void SendGameBatch(ReadOnlyMemory<byte> batch, GameTransportPriority priority, byte orderChannel) =>
        Session.SendFrame(new Frame
        {
            Reliability = Reliability.ReliableOrdered,
            OrderChannel = orderChannel,
            Buffer = batch
        }, priority == GameTransportPriority.Immediate
            ? RakNetSession.Priority.Immediate
            : RakNetSession.Priority.Normal);

    public void SendUnreliableGameBatch(ReadOnlyMemory<byte> batch) =>
        Session.SendFrame(new Frame { Reliability = Reliability.Unreliable, Buffer = batch }, RakNetSession.Priority.Normal);

    public void FlushOutgoing() => Session.FlushOutgoing();
    public void Disconnect() => Session.Disconnect();
}

internal sealed class NetherNetGameTransportConnection(NetherNetConnection connection) : IGameTransportConnection
{
    private int _hasGameIdentity;
    public NetherNetConnection Connection { get; } = connection;
    public string EndPoint => $"NetherNet/{Connection.NetworkId}";
    public bool IsClosed => Connection.IsClosed;
    public bool HasGameIdentity
    {
        get => Volatile.Read(ref _hasGameIdentity) != 0;
        set => Volatile.Write(ref _hasGameIdentity, value ? 1 : 0);
    }
    public bool RequiresGameServerTokenBinding => true;
    public NetherNetClientIdentity? ClientIdentity => Connection.ClientIdentity;
    public bool IncludesGamePacketIdentifier => false;

    public void SendGameBatch(ReadOnlyMemory<byte> batch, GameTransportPriority priority, byte orderChannel)
    {
        if (!Connection.SendReliable(batch.Span))
            Connection.Disconnect("NetherNet reliable send queue is unavailable.");
    }

    public void SendUnreliableGameBatch(ReadOnlyMemory<byte> batch)
    {
        if (!Connection.SendUnreliable(batch.Span))
            Connection.Disconnect("NetherNet unreliable send queue is unavailable.");
    }

    public void FlushOutgoing() => Connection.FlushOutgoing();
    public void Disconnect() => Connection.Disconnect("Bedrock session closed.");
}
