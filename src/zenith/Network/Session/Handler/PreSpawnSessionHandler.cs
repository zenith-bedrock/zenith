using Zenith.Network.Protocol;
using Zenith.Raknet.Stream;
using Zenith.Network.Packets;
using Zenith.World;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Estado entre StartGame e loading completo.
/// Pede colunas ao <see cref="Server.ServerContext.World"/> (leitura thread-safe);
/// <see cref="WorldProtocol"/> só transmite.
/// Por coluna: LevelChunk (base) → UpdateBlock dos overlays — nunca flush global de overlay.
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

            case (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET:
            case (int)ProtocolInfo.SERVERBOUND_LOADING_SCREEN_PACKET:
                return true;

            case (int)ProtocolInfo.DISCONNECT_PACKET:
                var disconnect = DataPacket.From<DisconnectPacket>(ref stream);
                session.Context.Logger.Error($"[DisconnectPacket] Reason: {disconnect.Reason}, Message: {disconnect.Message}");
                return false;

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

        var worldColumns = session.Context.World
            .GetRadiusAsync(centerX: 0, centerZ: 0, radius)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        session.Protocol.World.SendChunkRadiusUpdated(radius);

        foreach (var column in worldColumns)
        {
            ColumnTerrainEmitter.Emit(
                column,
                sendLevelChunk: bas => session.Protocol.World.SendLevelChunk(new ChunkColumn(
                    bas.Coord.X,
                    bas.Coord.Z,
                    bas.DimensionId,
                    bas.SubChunkCount,
                    bas.ExtraPayload)),
                sendUpdateBlock: (x, y, z, runtimeId) =>
                    session.Protocol.World.SendUpdateBlock(x, y, z, runtimeId));
        }

        session.Protocol.World.SendChunkPublisher(
            blockX: 0,
            blockY: Blocks.FlatSpawnY,
            blockZ: 0,
            radiusBlocks: radius * 16);
        session.Protocol.World.SendWorldSpawnPosition(x: 0, y: Blocks.FlatSpawnY, z: 0);
        session.Context.Logger.Debug("Chunks published (base + per-column overlays), waiting for spawn response");
        session.Protocol.World.SendSpawnComplete();

        session.SetHandler(new SpawnResponseSessionHandler());
    }
}
