using Zenith.Raknet.Stream;
using Zenith.Network.Packets;
using Zenith.Network.Protocol;

namespace Zenith.Network.Session.Handler;

/// <summary>
/// Estado entre "cliente recebeu o StartGame" e "cliente saiu da tela de loading".
///
/// Decide raio/grade de chunks (temporariamente aqui até existir ChunkPublisher de domínio);
/// <see cref="WorldProtocol"/> apenas transmite as colunas e os pacotes de spawn já decididos.
/// </summary>
class PreSpawnSessionHandler : ISessionHandler
{
    /// <summary>
    /// Raio hardcoded (em chunks) da grade de chunks vazios enviada ao redor do spawn,
    /// independente do que o cliente pedir. Suficiente pra desbloquear o loading screen sem
    /// esperar um PlayerChunkLoader / World de verdade existir.
    /// </summary>
    private const int SpawnChunkRadius = 4;

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
        var radius = Math.Min(request.Radius, SpawnChunkRadius);

        session.Context.Logger.Debug($"RequestChunkRadiusPacket: requested={request.Radius}, using={radius}");

        // Decisão de gameplay (temporária neste handler): quais colunas enviar.
        var emptyColumn = ChunkUtils.BuildEmptyOverworldPayload();
        var columns = new List<ChunkColumn>();
        for (var x = -radius; x <= radius; x++)
        {
            for (var z = -radius; z <= radius; z++)
            {
                columns.Add(new ChunkColumn(x, z, DimensionId: 0, SubChunkCount: 0, emptyColumn));
            }
        }

        // Protocol só transmite. Spawn do StartGame é (0, 8, 0).
        session.Protocol.World.SendChunkRadiusUpdated(radius);
        session.Protocol.World.PublishChunks(columns);
        session.Protocol.World.SendChunkPublisher(blockX: 0, blockY: 8, blockZ: 0, radiusBlocks: radius * 16);
        session.Protocol.World.SendWorldSpawnPosition(x: 0, y: 8, z: 0);
        session.Context.Logger.Debug("Chunks published, sending spawn notification and waiting for spawn response");
        session.Protocol.World.SendSpawnComplete();

        session.SetHandler(new SpawnResponseSessionHandler());
    }
}
