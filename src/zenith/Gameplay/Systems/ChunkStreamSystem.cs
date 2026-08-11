using System.Collections.Generic;
using System.Diagnostics;
using Zenith.Gameplay.Runtime;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Emite colunas flat novas quando o jogador muda de chunk / tem buracos no disco de view.
/// I/O de coluna é async (não bloqueia o tick); conclusões retornam pela fila do tracker e só
/// são validadas/transmitidas no tick.
/// Colunas fora do raio são esquecidas quando o centro muda para o cliente poder
/// revalidar overlays após unload (smoke 6) — não é VisibilitySystem.
/// </summary>
sealed class ChunkStreamSystem : IGameSystem
{
    /// <summary>Máximo de novos loads iniciados por jogador por tick (anti-burst).</summary>
    public const int MaxStartsPerTick = 8;
    /// <summary>Bound ready-spawn LevelChunk publication so a configured large radius cannot stall one tick.</summary>
    public const int MaxPreSpawnColumnsPerTick = 32;
    /// <summary>Bound ready-spawn overlay publication independently from base terrain columns.</summary>
    public const int MaxPreSpawnOverlaysPerTick = 128;

    private readonly World.World _world;
    private readonly List<(int X, int Z)> _knownScratch = new();
    private readonly Dictionary<global::Zenith.Player.Player, PreSpawnPublication> _preSpawnPublications = new();
    private readonly List<global::Zenith.Player.Player> _stalePreSpawnScratch = new();

    public ChunkStreamSystem(World.World world)
    {
        _world = world;
    }
    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        RemoveOfflinePreSpawnPublications(online);

        foreach (var player in online)
        {
            StartPendingPreSpawn(player);
            DrainCompletedPreSpawn(player);
            PumpPreSpawnPublication(player);
            ApplySpawnReady(player, online);
            DrainCompletedStreams(player);
            if (!player.IsInGame && !player.IsSpawning) continue;
            var radius = player.Chunks.Radius;
            if (radius < 0) continue;

            if (player.Chunks.NeedsOverlayResync)
            {
                player.Chunks.CopyKnown(_knownScratch);
                ColumnSend.EmitOverlaysToSession(player.Session, _world, _knownScratch);
                player.Chunks.NeedsOverlayResync = false;
            }

            var cx = PlayerChunkTracker.BlockToChunk(player.PositionX);
            var cz = PlayerChunkTracker.BlockToChunk(player.PositionZ);

            if (player.Chunks.PublisherCenterChanged(cx, cz))
            {
                player.Chunks.ForgetOutsideRadius(cx, cz, radius);
                player.Session.Protocol.World.SendChunkPublisher(
                    blockX: (int)Math.Floor(player.PositionX),
                    blockY: (int)Math.Floor(player.PositionY),
                    blockZ: (int)Math.Floor(player.PositionZ),
                    radiusBlocks: Math.Max(radius, 0) * 16);
                player.Session.Context.Logger.Info(
                    $"ChunkStream publisher → chunk {cx},{cz} radius={radius} for {player.Username}");
            }

            var started = 0;
            PlayerChunkTracker.ForEachInSquare(cx, cz, radius, (x, z) =>
            {
                if (started >= MaxStartsPerTick) return;
                if (!player.Chunks.TryBegin(x, z, out var epoch)) return;
                started++;
                StartStream(player, x, z, epoch);
            });

            if (started > 0)
            {
                player.Session.Context.Logger.Info(
                    $"ChunkStream starting {started} columns near {cx},{cz} for {player.Username}");
            }
        }
    }

    private void StartStream(global::Zenith.Player.Player player, int chunkX, int chunkZ, int epoch)
    {
        player.Session.Context.Logger.Debug(
            $"ChunkStream start @ {chunkX},{chunkZ} for {player.Username}");
        _ = StreamAsync(player.Chunks, chunkX, chunkZ, epoch);
    }

    private static void ApplySpawnReady(
        global::Zenith.Player.Player player,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (!player.TryConsumeSpawnReady())
            return;

        if (!player.IsSpawning || player.IsInGame || player.Session.RakSession.IsClosed)
            return;

        player.Session.Context.Logger.Info($"{player.Username} finished spawning; entering in-game phase.");
        PlayerVisibility.AnnounceJoin(player, online);
        player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
        player.Session.Protocol.Inventory.SendUiInventoryContent(player);
        player.Session.SetHandler(new InGameSessionHandler());
    }

    private void StartPendingPreSpawn(global::Zenith.Player.Player player)
    {
        if (!player.Chunks.TryConsumePreSpawnRequest(out var request))
            return;

        var readyRadius = Math.Min(request.ViewRadius, player.Session.Context.Config.World.SpawnReadyRadius);
        var snapshot = new PlayerChunkTracker.PreSpawnSnapshot(
            request.ViewRadius,
            readyRadius,
            PlayerChunkTracker.BlockToChunk(player.PositionX),
            PlayerChunkTracker.BlockToChunk(player.PositionZ),
            (int)MathF.Floor(player.PositionX),
            (int)MathF.Floor(player.PositionY),
            (int)MathF.Floor(player.PositionZ));
        player.Chunks.Radius = request.ViewRadius;

        var columnCount = (readyRadius * 2 + 1) * (readyRadius * 2 + 1);
        player.Session.Context.Logger.Info(
            $"PreSpawn loading ready-disk radius {readyRadius} ({columnCount} columns, view={request.ViewRadius}) " +
            $"for {player.Username} @ chunk {snapshot.CenterChunkX},{snapshot.CenterChunkZ}…");
        _ = LoadPreSpawnAsync(player.Chunks, snapshot);
    }

    private async Task LoadPreSpawnAsync(PlayerChunkTracker tracker, PlayerChunkTracker.PreSpawnSnapshot snapshot)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var columns = await _world.GetRadiusAsync(
                snapshot.CenterChunkX, snapshot.CenterChunkZ, snapshot.ReadyRadius).ConfigureAwait(false);
            watch.Stop();
            tracker.CompletePreSpawn(PlayerChunkTracker.PreSpawnCompletion.Success(snapshot, columns, watch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            watch.Stop();
            tracker.CompletePreSpawn(PlayerChunkTracker.PreSpawnCompletion.Failure(snapshot, ex.Message, watch.ElapsedMilliseconds));
        }
    }

    private void DrainCompletedPreSpawn(global::Zenith.Player.Player player)
    {
        if (!player.Chunks.TryConsumePreSpawnCompletion(out var completion))
            return;

        if (player.Session.RakSession.IsClosed || player.IsInGame || player.IsSpawning)
            return;

        if (!completion.Succeeded)
        {
            player.Session.Context.Logger.Error($"PreSpawn chunk load failed for {player.Username}: {completion.Error}");
            player.Session.Disconnect();
            return;
        }

        _preSpawnPublications[player] = new PreSpawnPublication(completion);
        player.Session.Context.Logger.Info(
            $"PreSpawn loaded {completion.Columns!.Count} columns for {player.Username} in " +
            $"{completion.LoadElapsedMilliseconds} ms; publishing from gameplay tick…");
    }

    private void PumpPreSpawnPublication(global::Zenith.Player.Player player)
    {
        if (!_preSpawnPublications.TryGetValue(player, out var publication))
            return;

        if (player.Session.RakSession.IsClosed || player.IsInGame || player.IsSpawning)
        {
            _preSpawnPublications.Remove(player);
            return;
        }

        var session = player.Session;
        var snapshot = publication.Completion.Snapshot;
        var columns = publication.Completion.Columns!;
        if (!publication.PublisherSent)
        {
            session.Protocol.World.SendChunkPublisher(
                snapshot.BlockX, snapshot.BlockY, snapshot.BlockZ,
                radiusBlocks: Math.Max(snapshot.ViewRadius, 0) * 16);
            _ = player.Chunks.PublisherCenterChanged(snapshot.CenterChunkX, snapshot.CenterChunkZ);
            publication.PublisherSent = true;
        }

        var columnsSent = 0;
        while (publication.ColumnIndex < columns.Count && columnsSent < MaxPreSpawnColumnsPerTick)
        {
            var column = columns[publication.ColumnIndex++];
            var bas = column.Base;
            session.Protocol.World.PublishChunks([
                new ChunkColumn(bas.Coord.X, bas.Coord.Z, bas.DimensionId, bas.SubChunkCount, bas.ExtraPayload)
            ]);
            player.Chunks.RememberMany([(bas.Coord.X, bas.Coord.Z)]);
            publication.PayloadBytes += bas.ExtraPayload.LongLength;
            publication.Envelopes++;
            columnsSent++;
        }

        var overlaysSent = 0;
        while (publication.ColumnIndex == columns.Count && publication.OverlayColumnIndex < columns.Count &&
               overlaysSent < MaxPreSpawnOverlaysPerTick)
        {
            var overlays = columns[publication.OverlayColumnIndex].Overlays;
            while (publication.OverlayIndex < overlays.Count && overlaysSent < MaxPreSpawnOverlaysPerTick)
            {
                var overlay = overlays[publication.OverlayIndex++];
                session.Protocol.World.SendUpdateBlock(overlay.X, overlay.Y, overlay.Z, overlay.BlockRuntimeId);
                overlaysSent++;
            }

            if (publication.OverlayIndex == overlays.Count)
            {
                publication.OverlayColumnIndex++;
                publication.OverlayIndex = 0;
            }
        }

        if (publication.ColumnIndex != columns.Count || publication.OverlayColumnIndex != columns.Count)
            return;

        // PocketMine PreSpawn: inventory before PLAYER_SPAWN (not only after initialized).
        session.Protocol.World.SendWorldSpawnPosition(snapshot.BlockX, snapshot.BlockY, snapshot.BlockZ);
        session.Protocol.Inventory.SendInventoryContent(player.Inventory);
        session.Protocol.Inventory.SendUiInventoryContent(player);
        session.Protocol.Entity.SendMovePlayerTeleport(
            (ulong)player.RuntimeId,
            player.PositionX, player.PositionY, player.PositionZ,
            player.Pitch, player.Yaw, player.HeadYaw);
        session.Protocol.World.SendSpawnComplete();
        session.Context.Logger.Info(
            $"PreSpawn publish done for {player.Username}: {columns.Count} columns, " +
            $"{publication.Envelopes} envelopes, ~{publication.PayloadBytes} payload bytes — " +
            "waiting SetLocalPlayerAsInitialized");

        _preSpawnPublications.Remove(player);
        session.SetHandler(new SpawnResponseSessionHandler());
    }

    private void RemoveOfflinePreSpawnPublications(IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (_preSpawnPublications.Count == 0)
            return;

        _stalePreSpawnScratch.Clear();
        foreach (var player in _preSpawnPublications.Keys)
        {
            if (!online.Contains(player))
                _stalePreSpawnScratch.Add(player);
        }

        foreach (var player in _stalePreSpawnScratch)
            _preSpawnPublications.Remove(player);
    }

    private sealed class PreSpawnPublication
    {
        public PreSpawnPublication(PlayerChunkTracker.PreSpawnCompletion completion) => Completion = completion;

        public PlayerChunkTracker.PreSpawnCompletion Completion { get; }
        public bool PublisherSent { get; set; }
        public int ColumnIndex { get; set; }
        public int OverlayColumnIndex { get; set; }
        public int OverlayIndex { get; set; }
        public long PayloadBytes { get; set; }
        public int Envelopes { get; set; }
    }

    private async Task StreamAsync(PlayerChunkTracker tracker, int chunkX, int chunkZ, int epoch)
    {
        try
        {
            var column = await _world.GetOrCreateColumnAsync(chunkX, chunkZ).ConfigureAwait(false);
            tracker.CompleteStream(chunkX, chunkZ, epoch, column);
        }
        catch (Exception ex)
        {
            tracker.FailStream(chunkX, chunkZ, epoch, ex.Message);
        }
    }

    private void DrainCompletedStreams(global::Zenith.Player.Player player)
    {
        var tracker = player.Chunks;
        while (tracker.TryConsumeCompletedStream(out var completion))
        {
            if (!tracker.IsStreamCurrent(completion.ChunkX, completion.ChunkZ, completion.Epoch))
                continue;

            if (!completion.Succeeded)
            {
                if (tracker.TryAbandon(completion.ChunkX, completion.ChunkZ, completion.Epoch))
                {
                    player.Session.Context.Logger.Warning(
                        $"Chunk stream failed for {player.Username} @ {completion.ChunkX},{completion.ChunkZ}: {completion.Error}");
                }
                continue;
            }

            if (!player.IsInGame && !player.IsSpawning)
            {
                tracker.TryAbandon(completion.ChunkX, completion.ChunkZ, completion.Epoch);
                continue;
            }

            ColumnSend.EmitToSession(player.Session, completion.Column);
        }
    }
}
