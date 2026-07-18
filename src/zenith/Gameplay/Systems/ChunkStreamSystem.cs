using Zenith.Gameplay.Runtime;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Emite colunas flat novas quando o jogador muda de chunk / tem buracos no disco de view.
/// I/O de coluna é async (não bloqueia o tick); Protocol.Send sob o lock RakNet existente.
/// Colunas fora do raio são esquecidas quando o centro muda para o cliente poder
/// revalidar overlays após unload (smoke 6) — não é VisibilitySystem.
/// </summary>
sealed class ChunkStreamSystem : IGameSystem
{
    /// <summary>Máximo de novos loads iniciados por jogador por tick (anti-burst).</summary>
    public const int MaxStartsPerTick = 8;

    private readonly PlayerManager _players;
    private readonly World.World _world;
    private readonly List<(int X, int Z)> _knownScratch = new();

    public ChunkStreamSystem(PlayerManager players, World.World world)
    {
        _players = players;
        _world = world;
    }

    public void Tick(GameClock clock) => Tick(clock, _players.Online);

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (!player.IsInGame) continue;
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
            }

            var started = 0;
            PlayerChunkTracker.ForEachInSquare(cx, cz, radius, (x, z) =>
            {
                if (started >= MaxStartsPerTick) return;
                if (!player.Chunks.TryBegin(x, z, out var epoch)) return;
                started++;
                StartStream(player, x, z, epoch);
            });
        }
    }

    private void StartStream(global::Zenith.Player.Player player, int chunkX, int chunkZ, int epoch)
    {
        _ = StreamAsync(player, chunkX, chunkZ, epoch);
    }

    private async Task StreamAsync(global::Zenith.Player.Player player, int chunkX, int chunkZ, int epoch)
    {
        try
        {
            var column = await _world.GetOrCreateColumnAsync(chunkX, chunkZ).ConfigureAwait(false);
            if (!player.IsInGame) return;
            if (!player.Chunks.IsStreamCurrent(chunkX, chunkZ, epoch)) return;
            ColumnSend.EmitToSession(player.Session, column);
        }
        catch (Exception ex)
        {
            if (player.Chunks.TryAbandon(chunkX, chunkZ, epoch))
            {
                player.Session.Context.Logger.Warning(
                    $"Chunk stream failed for {player.Username} @ {chunkX},{chunkZ}: {ex.Message}");
            }
        }
    }
}
