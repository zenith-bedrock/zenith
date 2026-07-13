using Zenith.Raknet.Stream;
using Zenith.Network.Protocol;

namespace Zenith.Session.Handler;

/// <summary>
/// Estado entre "cliente recebeu o StartGamePacket" e "cliente saiu da tela de loading".
///
/// O cliente só sai da tela de "Loading world" depois de receber chunk data cobrindo a área
/// ao redor do spawn e um <see cref="NetworkChunkPublisherUpdatePacket"/> - só status packet
/// não é suficiente, por mais que StartGame já tenha sido mandado. Esse handler existe pra
/// reagir ao <see cref="RequestChunkRadiusPacket"/> que o cliente manda logo depois de
/// StartGame: nesse momento a gente manda a grade de chunks (falsos, só ar, já que não existe
/// World/Chunk de verdade ainda) e o publisher update, e só então o PlayStatus de spawn.
///
/// Depois disso troca pra <see cref="SpawnResponseSessionHandler"/>, que espera a confirmação
/// final do cliente antes de considerar ele "in-game" de verdade.
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

            // O cliente já manda esses antes de qualquer controle estar liberado (ou antes
            // de ter chunk data pra valer). Não tem o que fazer com eles ainda; só evita
            // poluir o log como "unhandled" enquanto isso não muda.
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

        // Tudo num único SendDataPacket: vira um GamePacket só (uma frame raknet, com split
        // automático se passar do MTU), em vez de uma ida de rede por pacote - é isso que
        // realmente "expedita" o spawn em vez de só resolver o travamento.
        var packets = new List<DataPacket> { new ChunkRadiusUpdatedPacket { Radius = radius } };

        var emptyColumn = ChunkUtils.BuildEmptyOverworldPayload();
        for (var x = -radius; x <= radius; x++)
        {
            for (var z = -radius; z <= radius; z++)
            {
                packets.Add(new LevelChunkPacket
                {
                    ChunkX = x,
                    ChunkZ = z,
                    DimensionId = 0,
                    SubChunkCount = 0,
                    ExtraPayload = emptyColumn
                });
            }
        }

        // Spawn do StartGamePacket é (0, 8, 0); mantém o publisher update e o spawn point
        // centrados no mesmo lugar.
        packets.Add(new NetworkChunkPublisherUpdatePacket { BlockX = 0, BlockY = 8, BlockZ = 0, Radius = radius * 16 });
        packets.Add(new SetSpawnPositionPacket { SpawnType = SetSpawnPositionPacket.TYPE_WORLD_SPAWN, X = 0, Y = 8, Z = 0 });

        session.Context.Logger.Debug("Chunks published, sending spawn notification and waiting for spawn response");
        packets.Add(new PlayStatusPacket { Status = 3 }); // PLAYER_SPAWN

        session.SendDataPacket(packets.ToArray());
        session.SetHandler(new SpawnResponseSessionHandler());
    }
}
