using Zenith.Protocol;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Stream;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>
/// Estado entre StartGame e loading completo.
/// Ordem no wire: ChunkRadiusUpdated → NetworkChunkPublisherUpdate → LevelChunks (batched) → PlayStatus.
/// Sem NetworkChunkPublisherUpdate o cliente ignora terrain mesmo com LevelChunks válidos.
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

        if (session.Player is not null)
            session.Player.Chunks.Radius = radius;

        session.Protocol.World.SendChunkRadiusUpdated(radius);

        if (Interlocked.Exchange(ref session.PreSpawnLoadStarted, 1) != 0)
        {
            session.Context.Logger.Debug("Ignoring duplicate RequestChunkRadius during PreSpawn load.");
            return;
        }

        // Fire-and-forget: não bloquear a receive thread com storage I/O.
        _ = CompleteSpawnAsync(session, radius);
    }

    private static async Task CompleteSpawnAsync(NetworkSession session, int radius)
    {
        try
        {
            var worldColumns = await session.Context.World
                .GetRadiusAsync(centerX: 0, centerZ: 0, radius)
                .ConfigureAwait(false);

            if (session.Player is null)
                return;

            // Publisher before LevelChunks — radius is in blocks, not chunks.
            session.Protocol.World.SendChunkPublisher(
                blockX: 0,
                blockY: Blocks.FlatSpawnY,
                blockZ: 0,
                radiusBlocks: radius * 16);

            var remembered = new List<(int X, int Z)>(worldColumns.Count);
            var batch = new List<ChunkColumn>(WorldProtocol.LevelChunkBatchSize);

            void FlushBatch()
            {
                if (batch.Count == 0) return;
                session.Protocol.World.PublishChunks(batch);
                batch.Clear();
            }

            foreach (var column in worldColumns)
            {
                var bas = column.Base;
                batch.Add(new ChunkColumn(
                    bas.Coord.X,
                    bas.Coord.Z,
                    bas.DimensionId,
                    bas.SubChunkCount,
                    bas.ExtraPayload));
                remembered.Add((bas.Coord.X, bas.Coord.Z));

                if (batch.Count >= WorldProtocol.LevelChunkBatchSize)
                    FlushBatch();
            }

            FlushBatch();

            // Remember as soon as LevelChunks are out so live UpdateBlock fan-out
            // can reach this joiner during overlay send / SpawnResponse (§14).
            session.Player.Chunks.RememberMany(remembered);
            session.Player.Chunks.PublisherCenterChanged(0, 0);

            // Overlays after base terrain (same order as ColumnTerrainEmitter).
            foreach (var column in worldColumns)
            {
                var overlays = column.Overlays;
                for (var i = 0; i < overlays.Count; i++)
                {
                    var o = overlays[i];
                    session.Protocol.World.SendUpdateBlock(o.X, o.Y, o.Z, o.BlockRuntimeId);
                }
            }

            session.Protocol.World.SendWorldSpawnPosition(x: 0, y: Blocks.FlatSpawnY, z: 0);
            session.Context.Logger.Debug("Chunks published (publisher → batched LevelChunks → overlays), waiting for spawn response");
            session.Protocol.World.SendSpawnComplete();

            session.SetHandler(new SpawnResponseSessionHandler());
        }
        catch (Exception ex)
        {
            session.Context.Logger.Error($"PreSpawn chunk load failed: {ex.Message}");
            session.Disconnect();
        }
    }
}
