using Zenith.Protocol;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Stream;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>
/// Estado entre StartGame e loading completo.
/// Ordem (ADR §70): ChunkRadiusUpdated → NetworkChunkPublisherUpdate →
/// LevelChunks (ready-disk) → inventory seed → PlayStatus(PLAYER_SPAWN).
/// Ready-disk is <c>world.spawn-ready-radius</c>; ChunkStream fills the view ring while spawning.
/// </summary>
class PreSpawnSessionHandler : ISessionHandler
{
    public void OnEnable(NetworkSession session)
    {
        session.Context.Logger.Info($"{session.Player?.Username} entered pre-spawn stage, waiting for chunk radius request.");
    }

    public bool HandleDataPacket(NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
    {
        switch (header.Id)
        {
            case (int)ProtocolInfo.REQUEST_CHUNK_RADIUS_PACKET:
                HandleRequestChunkRadius(session, ref stream);
                return true;

            case (int)ProtocolInfo.CLIENT_CACHE_STATUS_PACKET:
            case (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
                return true;

            case (int)ProtocolInfo.DISCONNECT_PACKET:
                var disconnect = DataPacket.From<DisconnectPacket>(ref stream);
                session.Context.Logger.Info(
                    $"[DisconnectPacket] Reason: {disconnect.Reason}, Message: {disconnect.Message}");
                session.Disconnect();
                return true;

            default:
                return false;
        }
    }

    private static void HandleRequestChunkRadius(NetworkSession session, ref BinaryStream stream)
    {
        var request = DataPacket.From<RequestChunkRadiusPacket>(ref stream);
        var cap = session.Context.Config.World.SpawnChunkRadius;
        var radius = Math.Min(request.Radius, cap);

        session.Context.Logger.Debug($"RequestChunkRadiusPacket: requested={request.Radius}, using={radius}");

        session.Protocol.World.SendChunkRadiusUpdated(radius);

        if (Interlocked.Exchange(ref session.PreSpawnLoadStarted, 1) != 0)
        {
            session.Context.Logger.Debug("Ignoring duplicate RequestChunkRadius during PreSpawn load.");
            return;
        }

        if (session.Player is null || !session.Player.Chunks.TrySubmitPreSpawn(radius))
        {
            session.Context.Logger.Debug("Ignored PreSpawn request after session/player state changed.");
            return;
        }

        // The gameplay owner captures Player state and starts storage I/O on its next tick.
        // This handler only accepts protocol input and publishes the protocol-only radius reply.
    }
}
