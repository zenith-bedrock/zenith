using Zenith.NetherNet.Axolotl.Negotiation;
using Zenith.NetherNet.Axolotl.Protocol;
using Zenith.NetherNet.Axolotl.Protocol.Framing;
using Zenith.NetherNet.Axolotl.Session;
using Zenith.NetherNet.Axolotl.Transport;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Axolotl.Server;

/// <summary>Owns every open <see cref="Session.Session"/> and pumps them each tick (port of <c>SessionManager</c>).</summary>
public sealed class SessionManager
{
    public const int PacketsPerTickLimit = 64;
    public const double StatsInterval = 1.0;

    private readonly Dictionary<int, Session.Session> _sessions = new();
    private int _nextSessionId;
    private readonly Segmenter _segmenter;
    private long _bytesSentDiff;
    private long _bytesReceivedDiff;
    private double? _nextStatsTime;

    private readonly IServerEventListener _listener;
    private readonly ConnectionBudgetConfiguration _budget;
    private readonly INetherNetLogger _logger;

    public SessionManager(IServerEventListener listener, ConnectionBudgetConfiguration budget, INetherNetLogger logger)
    {
        _listener = listener;
        _budget = budget;
        _logger = logger;
        _segmenter = new Segmenter(budget.MaxMessageSize - Segmenter.HeaderSize);
    }

    public Session.Session Open(EstablishedPeer peer)
    {
        var session = new Session.Session(_nextSessionId++, peer.Transport, peer.Channels, peer.Address,
            peer.ClientAddress, SegmenterFor(peer.Channels), _budget);
        var id = session.Id;
        _logger.Debug($"Session {id} opened for network id {peer.NetworkId} from {peer.Address}{(peer.ClientAddress is not null ? $" (client {peer.ClientAddress})" : "")}");
        _sessions[id] = session;

        try
        {
            _listener.OnSessionOpen(id, peer.NetworkId, peer.Address, peer.ClientAddress, peer.Identity);
        }
        catch
        {
            _sessions.Remove(id);
            session.ForciblyDisconnect(DisconnectReason.RejectedByHost);
            throw;
        }
        return session;
    }

    // Both channels must accept the segment size, so the smaller negotiated maximum message size wins.
    private Segmenter SegmenterFor(ChannelPair channels)
    {
        var segmentPayloadSize = Math.Min(channels.Reliable.MaxMessageSize, channels.Unreliable.MaxMessageSize) - Segmenter.HeaderSize;
        return segmentPayloadSize >= 1 && segmentPayloadSize < _segmenter.MaxSegmentPayload
            ? new Segmenter(segmentPayloadSize)
            : _segmenter;
    }

    public void Tick(double now)
    {
        foreach (var (id, session) in _sessions.ToArray())
        {
            foreach (var receiptId in session.TakeAcknowledgedReceipts())
                _listener.OnPacketAck(id, receiptId);

            session.Update(now);

            if (session.IsFullyDisconnected)
            {
                RemoveSession(session, DisconnectReason.ServerDisconnect);
                continue;
            }
            if (session.IsConnected)
                ProcessIncoming(session);
        }

        _nextStatsTime ??= now + StatsInterval;
        if (now >= _nextStatsTime)
        {
            _nextStatsTime = now + StatsInterval;
            ReportStats();
        }
    }

    private void ProcessIncoming(Session.Session session)
    {
        var id = session.Id;
        for (var i = 0; i < PacketsPerTickLimit; i++)
        {
            if (!_listener.CanAcceptPackets())
                return;

            ReceivedMessage? message;
            try
            {
                message = session.Receive();
            }
            catch (SessionException e)
            {
                _logger.Debug($"Session {id} failed while reading: {e.Message}");
                RemoveSession(session, DisconnectReason.BadData);
                return;
            }
            if (message is null)
                return;

            _listener.OnPacketReceive(id, message.Payload, message.Reliability);

            if (session.IsFullyDisconnected)
            {
                RemoveSession(session, DisconnectReason.ServerDisconnect);
                return;
            }
            if (!session.IsConnected)
                return;
        }
    }

    private void CollectBandwidth(Session.Session session)
    {
        var (sent, received) = session.TakeBandwidth();
        _bytesSentDiff += sent;
        _bytesReceivedDiff += received;
    }

    private void ReportStats()
    {
        foreach (var session in _sessions.Values)
            CollectBandwidth(session);

        if (_bytesSentDiff > 0 || _bytesReceivedDiff > 0)
        {
            _listener.OnBandwidthStatsUpdate(_bytesSentDiff, _bytesReceivedDiff);
            _bytesSentDiff = 0;
            _bytesReceivedDiff = 0;
        }

        foreach (var (id, session) in _sessions.ToArray())
        {
            var ping = session.TakePingChange();
            if (ping is not null)
                _listener.OnPingMeasure(id, ping.Value);
        }
    }

    private void RemoveSession(Session.Session session, DisconnectReason reason)
    {
        var id = session.Id;
        if (!_sessions.ContainsKey(id))
            return;

        session.ForciblyDisconnect(reason);
        reason = session.DisconnectReason ?? reason;
        CollectBandwidth(session);
        _sessions.Remove(id);
        _logger.Debug($"Session {id} closed: {reason.GetMessage()}");
        _listener.OnSessionClose(id, reason);
    }

    public Session.Session? GetSession(int id) => _sessions.GetValueOrDefault(id);

    public IReadOnlyList<Session.Session> GetSessions() => _sessions.Values.ToList();

    public int Count => _sessions.Count;

    public void Shutdown(DisconnectReason reason = DisconnectReason.ServerShutdown)
    {
        foreach (var session in _sessions.Values.ToArray())
            RemoveSession(session, reason);
    }
}
