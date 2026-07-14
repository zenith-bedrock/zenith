using Zenith.Raknet.Stream;
using Zenith.Network.Packets;
using Zenith.Network.Protocol;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Estado entre StartGame e loading completo.
/// Pede colunas ao <see cref="Server.ServerContext.World"/> (leitura thread-safe);
/// <see cref="WorldProtocol"/> só transmite.
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

        // Leitura fora do tick: World/IChunkStorage são thread-safe (ValueTask + ConcurrentDictionary).
        var worldColumns = session.Context.World
            .GetRadiusAsync(centerX: 0, centerZ: 0, radius)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        var columns = new List<ChunkColumn>(worldColumns.Count);
        foreach (var column in worldColumns)
        {
            columns.Add(new ChunkColumn(
                column.Coord.X,
                column.Coord.Z,
                column.DimensionId,
                column.SubChunkCount,
                column.ExtraPayload));
        }

        session.Protocol.World.SendChunkRadiusUpdated(radius);
        session.Protocol.World.PublishChunks(columns);
        session.Protocol.World.SendChunkPublisher(blockX: 0, blockY: 8, blockZ: 0, radiusBlocks: radius * 16);
        session.Protocol.World.SendWorldSpawnPosition(x: 0, y: 8, z: 0);
        session.Context.Logger.Debug("Chunks published, sending spawn notification and waiting for spawn response");
        session.Protocol.World.SendSpawnComplete();

        session.SetHandler(new SpawnResponseSessionHandler());
    }
}
