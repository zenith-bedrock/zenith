using Zenith.Gameplay.Runtime;
using Zenith.Network;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Emite colunas flat novas quando o jogador muda de chunk / tem buracos no disco de view.
/// I/O de coluna é async (não bloqueia o tick); Protocol.Send sob o lock RakNet existente.
/// </summary>
sealed class ChunkStreamSystem : IGameSystem
{
    /// <summary>Máximo de novos loads iniciados por jogador por tick (anti-burst).</summary>
    public const int MaxStartsPerTick = 8;

    private readonly PlayerManager _players;
    private readonly World.World _world;

    public ChunkStreamSystem(PlayerManager players, World.World world)
    {
        _players = players;
        _world = world;
    }

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            if (!player.IsInGame) continue;
            var radius = player.Chunks.Radius;
            if (radius < 0) continue;

            var cx = PlayerChunkTracker.BlockToChunk(player.PositionX);
            var cz = PlayerChunkTracker.BlockToChunk(player.PositionZ);

            if (player.Chunks.PublisherCenterChanged(cx, cz))
            {
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
                if (!player.Chunks.TryBegin(x, z)) return;
                started++;
                StartStream(player, x, z);
            });
        }
    }

    private void StartStream(global::Zenith.Player.Player player, int chunkX, int chunkZ)
    {
        _ = StreamAsync(player, chunkX, chunkZ);
    }

    private async Task StreamAsync(global::Zenith.Player.Player player, int chunkX, int chunkZ)
    {
        try
        {
            var column = await _world.GetOrCreateColumnAsync(chunkX, chunkZ).ConfigureAwait(false);
            if (!player.IsInGame) return;
            ColumnSend.EmitToSession(player.Session, column);
        }
        catch (Exception ex)
        {
            player.Chunks.Forget(chunkX, chunkZ);
            player.Session.Context.Logger.Warning(
                $"Chunk stream failed for {player.Username} @ {chunkX},{chunkZ}: {ex.Message}");
        }
    }
}
