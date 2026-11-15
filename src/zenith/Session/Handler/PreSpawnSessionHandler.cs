using Zenith.Protocol;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Binary;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>
/// Estado entre StartGame e loading completo.
/// Ordem (ADR §70): ChunkRadiusUpdated → NetworkChunkPublisherUpdate →
/// LevelChunks (center-first stream) → inventory seed → PlayStatus(PLAYER_SPAWN).
/// <c>world.spawn-ready-radius</c> is the central readiness threshold; the remaining view ring
/// continues streaming after PLAYER_SPAWN, as in PocketMine/Dragonfly.
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
                ChunkRadiusNegotiation.HandleRequest(session, ref stream, startPreSpawn: true);
                return true;

            case (int)ProtocolInfo.CLIENT_CACHE_STATUS_PACKET:
            case (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
            case (int)ProtocolInfo.CLIENT_CAMERA_AIM_ASSIST_PACKET:
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

}
