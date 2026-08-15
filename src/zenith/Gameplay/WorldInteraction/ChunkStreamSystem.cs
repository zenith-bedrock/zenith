using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Zenith.Gameplay.Runtime;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

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
    /// <summary>Maximum ready-spawn LevelChunk packets published by one player in one tick.</summary>
    public const int MaxPreSpawnColumnsPerTick = 32;
    /// <summary>Maximum completed post-spawn columns transmitted by one player in one tick.</summary>
    public const int MaxCompletedStreamsPerTick = 4;

    private readonly World.World _world;
    private readonly WorldGenerationDiagnostics? _generationDiagnostics;
    private readonly List<(int X, int Z)> _knownScratch = new();
    private readonly Dictionary<global::Zenith.Player.Player, PreSpawnPublication> _preSpawnPublications = new();
    private readonly List<global::Zenith.Player.Player> _stalePreSpawnScratch = new();

    public ChunkStreamSystem(World.World world, WorldGenerationDiagnostics? generationDiagnostics = null)
    {
        _world = world;
        _generationDiagnostics = generationDiagnostics;
    }
    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        RemoveOfflinePreSpawnPublications(online);

        foreach (var player in online)
        {
            StartPendingPreSpawn(player);
            PumpPreSpawnPublication(player);
            ApplySpawnReady(player, online);
            DrainCompletedStreams(player);
            if (!player.IsInGame && !player.IsSpawning) continue;
            // A pre-spawn publication release early (ADR §111 spawn-ready barrier) sets IsSpawning
            // while its own PreSpawnLoad is still streaming the remainder of the view in the
            // background. The regular per-tick view-streaming block below exists for post-load
            // re-streaming (the player moving to newly-visible chunks) — running it concurrently
            // with an active pre-spawn publication would TryBegin the same still-loading
            // coordinates a second time, double-publishing them through two separate budgets.
            if (_preSpawnPublications.ContainsKey(player)) continue;
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
        player.Session.Protocol.Inventory.SendArmorContent(player.Inventory);
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

        var readyColumnCount = (readyRadius * 2 + 1) * (readyRadius * 2 + 1);
        var viewColumnCount = (request.ViewRadius * 2 + 1) * (request.ViewRadius * 2 + 1);
        player.Session.Context.Logger.Info(
            $"PreSpawn streaming view radius {request.ViewRadius} ({viewColumnCount} columns), " +
            $"spawn-ready radius {readyRadius} ({readyColumnCount} central columns) " +
            $"for {player.Username} @ chunk {snapshot.CenterChunkX},{snapshot.CenterChunkZ}…");
        var load = new PreSpawnLoad(snapshot);
        _preSpawnPublications[player] = new PreSpawnPublication(load);
        _ = LoadPreSpawnAsync(load);
    }

    private async Task LoadPreSpawnAsync(PreSpawnLoad load)
    {
        var watch = Stopwatch.StartNew();
        var diagnosticsScope = _generationDiagnostics is null
            ? default
            : _generationDiagnostics.BeginPreSpawnLoad();
        using (diagnosticsScope)
        {
            try
            {
                await foreach (var column in _world.StreamRadiusAsync(
                                   load.Snapshot.CenterChunkX,
                                   load.Snapshot.CenterChunkZ,
                                   load.Snapshot.ViewRadius,
                                   load.CancellationToken).ConfigureAwait(false))
                    await load.Writer.WriteAsync(column, load.CancellationToken).ConfigureAwait(false);

                watch.Stop();
                load.Complete(null, watch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                watch.Stop();
                load.Complete(ex, watch.ElapsedMilliseconds);
            }
        }
    }

    private void PumpPreSpawnPublication(global::Zenith.Player.Player player)
    {
        if (!_preSpawnPublications.TryGetValue(player, out var publication))
            return;

        if (player.Session.RakSession.IsClosed
            || (player.IsInGame && !publication.SpawnReleased)
            || (player.IsSpawning && !publication.SpawnReleased))
        {
            publication.Load.Cancel();
            _preSpawnPublications.Remove(player);
            return;
        }

        PumpStreamingPreSpawn(player, publication);
    }

    private void PumpStreamingPreSpawn(
        global::Zenith.Player.Player player,
        PreSpawnPublication publication)
    {
        var load = publication.Load;
        var session = player.Session;
        var diagnosticsScope = _generationDiagnostics is null
            ? default
            : _generationDiagnostics.BeginPreSpawnPublish();
        using (diagnosticsScope)
        {
            var snapshot = load.Snapshot;
            if (!publication.PublisherSent)
            {
                session.Protocol.World.SendChunkPublisher(
                    snapshot.BlockX, snapshot.BlockY, snapshot.BlockZ,
                    radiusBlocks: Math.Max(snapshot.ViewRadius, 0) * 16);
                _ = player.Chunks.PublisherCenterChanged(snapshot.CenterChunkX, snapshot.CenterChunkZ);
                publication.PublisherSent = true;
            }

            var columnsSent = 0;
            var publishBudget = Math.Min(
                MaxPreSpawnColumnsPerTick,
                session.Context.Config.World.PreSpawnColumnsPerTick);
            while (columnsSent < publishBudget)
            {
                if (!publication.HasPendingColumn)
                {
                    if (!load.Reader.TryRead(out var pendingColumn))
                        break;
                    publication.PendingColumn = pendingColumn;
                }

                publication.HasPendingColumn = true;
                var orderChannel = publication.SpawnReleased
                    ? NetworkSession.WorldStreamOrderChannel
                    : NetworkSession.DefaultOrderChannel;
                if (!session.QueueWorldColumn(publication.PendingColumn, orderChannel, out var sequence))
                    return;

                var column = publication.PendingColumn;
                publication.HasPendingColumn = false;
                publication.ColumnIndex++;
                publication.LastQueuedSequence = sequence;
                if (Math.Abs(column.Base.Coord.X - snapshot.CenterChunkX) <= snapshot.ReadyRadius
                    && Math.Abs(column.Base.Coord.Z - snapshot.CenterChunkZ) <= snapshot.ReadyRadius)
                {
                    publication.ReadyColumnsQueued++;
                    if (publication.ReadyColumnsQueued >= publication.ReadyColumnsRequired)
                        publication.ReadyBarrierSequence = sequence;
                }
                player.Chunks.RememberMany([(column.Base.Coord.X, column.Base.Coord.Z)]);
                publication.PayloadBytes += column.Base.ExtraPayload.LongLength;
                publication.Envelopes++;
                _generationDiagnostics?.RecordPreSpawnPublished(1, column.Base.ExtraPayload.LongLength);
                columnsSent++;
            }

            if (load.Error is not null)
            {
                player.Session.Context.Logger.Error(
                    $"PreSpawn chunk load failed for {player.Username}: {load.Error.Message}");
                if (!publication.SpawnReleased)
                {
                    load.Cancel();
                    _preSpawnPublications.Remove(player);
                    player.Session.Disconnect();
                }
                else
                {
                    _preSpawnPublications.Remove(player);
                    load.Dispose();
                }
                return;
            }

            if (!publication.SpawnReleased)
            {
                if (publication.ReadyBarrierSequence == 0
                    || session.WorldStreamCompletedThrough < publication.ReadyBarrierSequence)
                    return;

                session.Protocol.World.SendWorldSpawnPosition(snapshot.BlockX, snapshot.BlockY, snapshot.BlockZ);
                session.Protocol.Inventory.SendInventoryContent(player.Inventory);
                session.Protocol.Inventory.SendUiInventoryContent(player);
                session.Protocol.Inventory.SendArmorContent(player.Inventory);
                session.Protocol.Entity.SendMovePlayerTeleport(
                    (ulong)player.RuntimeId,
                    player.PositionX, player.PositionY, player.PositionZ,
                    player.Pitch, player.Yaw, player.HeadYaw);
                session.Protocol.World.SendSpawnComplete();
                publication.SpawnReleased = true;
                session.Context.Logger.Info(
                    $"PreSpawn readiness reached for {player.Username}: " +
                    $"{publication.ReadyColumnsQueued}/{publication.ReadyColumnsRequired} central columns, " +
                    $"{publication.ColumnIndex} columns queued; continuing view stream in background " +
                    $"(load {load.LoadElapsedMilliseconds} ms)");
                session.SetHandler(new SpawnResponseSessionHandler());
            }

            if (load.Reader.Completion.IsCompleted
                && !publication.HasPendingColumn
                && session.WorldStreamIdle)
            {
                _preSpawnPublications.Remove(player);
                load.Dispose();
            }
        }
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
        {
            if (_preSpawnPublications.TryGetValue(player, out var publication))
                publication.Load.Cancel();
            _preSpawnPublications.Remove(player);
        }
    }

    private sealed class PreSpawnPublication
    {
        public PreSpawnPublication(PreSpawnLoad load)
        {
            Load = load;
            ReadyColumnsRequired = (load.Snapshot.ReadyRadius * 2 + 1) * (load.Snapshot.ReadyRadius * 2 + 1);
        }

        public PreSpawnLoad Load { get; }
        public bool PublisherSent { get; set; }
        public int ColumnIndex { get; set; }
        public long PayloadBytes { get; set; }
        public int Envelopes { get; set; }
        public bool HasPendingColumn { get; set; }
        public ColumnReadResult PendingColumn { get; set; }
        public int ReadyColumnsRequired { get; }
        public int ReadyColumnsQueued { get; set; }
        public long ReadyBarrierSequence { get; set; }
        public long LastQueuedSequence { get; set; }
        public bool SpawnReleased { get; set; }
    }

    private sealed class PreSpawnLoad : IDisposable
    {
        private readonly CancellationTokenSource _shutdown = new();

        public PreSpawnLoad(PlayerChunkTracker.PreSpawnSnapshot snapshot)
        {
            Snapshot = snapshot;
            var channel = Channel.CreateBounded<ColumnReadResult>(new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
            Reader = channel.Reader;
            Writer = channel.Writer;
        }

        public PlayerChunkTracker.PreSpawnSnapshot Snapshot { get; }
        public ChannelReader<ColumnReadResult> Reader { get; }
        public ChannelWriter<ColumnReadResult> Writer { get; }
        public CancellationToken CancellationToken => _shutdown.Token;
        public Exception? Error { get; private set; }
        public long LoadElapsedMilliseconds { get; private set; }

        public void Complete(Exception? error, long elapsedMilliseconds)
        {
            Error = error;
            LoadElapsedMilliseconds = elapsedMilliseconds;
            Writer.TryComplete(error);
        }

        public void Cancel() => _shutdown.Cancel();

        public void Dispose()
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }
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
        var published = 0;
        var publishBudget = Math.Min(
            MaxCompletedStreamsPerTick,
            player.Session.Context.Config.World.ChunkStreamColumnsPerTick);
        while (published < publishBudget && tracker.TryConsumeCompletedStream(out var completion))
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

            if (!player.Session.QueueWorldColumn(completion.Column, NetworkSession.WorldStreamOrderChannel))
            {
                if (tracker.TryAbandon(completion.ChunkX, completion.ChunkZ, completion.Epoch))
                    _generationDiagnostics?.RecordDropped();
                continue;
            }

            published++;
        }
    }
}
