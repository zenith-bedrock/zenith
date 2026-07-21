using System.Diagnostics;
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

    private static async Task CompleteSpawnAsync(NetworkSession session, int viewRadius)
    {
        var readyRadius = Math.Min(viewRadius, session.Context.Config.World.SpawnReadyRadius);
        var columnCount = (readyRadius * 2 + 1) * (readyRadius * 2 + 1);
        try
        {
            if (session.Player is null)
                return;

            var centerX = PlayerChunkTracker.BlockToChunk(session.Player.PositionX);
            var centerZ = PlayerChunkTracker.BlockToChunk(session.Player.PositionZ);
            var blockX = (int)MathF.Floor(session.Player.PositionX);
            var blockY = (int)MathF.Floor(session.Player.PositionY);
            var blockZ = (int)MathF.Floor(session.Player.PositionZ);

            session.Context.Logger.Info(
                $"PreSpawn loading ready-disk radius {readyRadius} ({columnCount} columns, view={viewRadius}) " +
                $"for {session.Player.Username} @ chunk {centerX},{centerZ}…");

            var worldColumns = await session.Context.World
                .GetRadiusAsync(centerX, centerZ, readyRadius)
                .ConfigureAwait(false);

            if (session.Player is null)
                return;

            session.Context.Logger.Info(
                $"PreSpawn loaded {worldColumns.Count} columns for {session.Player.Username}, publishing…");

            // Publisher before LevelChunks — advertise view radius; ChunkStream fills the ring.
            session.Protocol.World.SendChunkPublisher(
                blockX,
                blockY,
                blockZ,
                radiusBlocks: Math.Max(viewRadius, 0) * 16);

            var remembered = new List<(int X, int Z)>(worldColumns.Count);
            var batch = new List<ChunkColumn>(WorldProtocol.LevelChunkBatchSize);
            var publishWatch = Stopwatch.StartNew();
            long payloadBytes = 0;
            var envelopes = 0;

            async Task FlushBatchAsync()
            {
                if (batch.Count == 0) return;
                for (var i = 0; i < batch.Count; i++)
                    payloadBytes += batch[i].ExtraPayload.LongLength;
                session.Protocol.World.PublishChunks(batch);
                envelopes++;
                batch.Clear();
                await Task.Yield();
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
                    await FlushBatchAsync().ConfigureAwait(false);
            }

            await FlushBatchAsync().ConfigureAwait(false);
            publishWatch.Stop();

            session.Player.Chunks.RememberMany(remembered);
            session.Player.Chunks.PublisherCenterChanged(centerX, centerZ);

            foreach (var column in worldColumns)
            {
                var overlays = column.Overlays;
                for (var i = 0; i < overlays.Count; i++)
                {
                    var o = overlays[i];
                    session.Protocol.World.SendUpdateBlock(o.X, o.Y, o.Z, o.BlockRuntimeId);
                }
            }

            session.Protocol.World.SendWorldSpawnPosition(x: blockX, y: blockY, z: blockZ);

            // PocketMine PreSpawn: inventory before PLAYER_SPAWN (not only after initialized).
            session.Protocol.Inventory.SendInventoryContent(session.Player.Inventory);
            session.Protocol.Inventory.SendUiInventoryContent(session.Player);

            // Snap camera to healed/authoritative feet (API takes domain feet).
            session.Protocol.Entity.SendMovePlayerTeleport(
                (ulong)session.Player.RuntimeId,
                session.Player.PositionX,
                session.Player.PositionY,
                session.Player.PositionZ,
                session.Player.Pitch,
                session.Player.Yaw,
                session.Player.HeadYaw);

            session.Context.Logger.Info(
                $"PreSpawn publish done for {session.Player.Username}: " +
                $"{worldColumns.Count} columns, {envelopes} envelopes, ~{payloadBytes} payload bytes, " +
                $"{publishWatch.ElapsedMilliseconds} ms — waiting SetLocalPlayerAsInitialized");
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
