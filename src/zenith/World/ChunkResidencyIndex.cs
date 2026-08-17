using System.Collections.Concurrent;

namespace Zenith.World;

/// <summary>
/// Incremental per-chunk viewer refcount (ADR §114) — how many currently-online players have chunk
/// (cx, cz) in their <see cref="Player.PlayerChunkTracker"/> known set right now. Owned by
/// <see cref="World"/>, mutated by <see cref="Gameplay.WorldInteraction.ChunkStreamSystem"/> at every
/// point it adds/removes a chunk from a player's known set, plus on disconnect cleanup.
/// <para/>
/// Counts are never removed from the backing map, only decremented to 0 — <see cref="Acquire"/> and
/// <see cref="Release"/> can race each other across the GameLoop thread (ChunkStreamSystem) and a
/// network thread (disconnect cleanup via <c>PlayerQuitEvent</c>); a plain atomic increment/decrement
/// with no removal step keeps that race-free without a lock. The dictionary's size is bounded by
/// "distinct chunks touched across the process lifetime," which is far smaller than the
/// overlay/chest dataset this feature exists to bound.
/// </summary>
sealed class ChunkResidencyIndex
{
    private readonly ConcurrentDictionary<(int Cx, int Cz), int> _viewers = new();

    public void Acquire(int chunkX, int chunkZ) =>
        _viewers.AddOrUpdate((chunkX, chunkZ), 1, static (_, count) => count + 1);

    public void Release(int chunkX, int chunkZ) =>
        _viewers.AddOrUpdate((chunkX, chunkZ), 0, static (_, count) => Math.Max(0, count - 1));

    public bool HasViewers(int chunkX, int chunkZ) =>
        _viewers.TryGetValue((chunkX, chunkZ), out var count) && count > 0;

    public int ViewerCount(int chunkX, int chunkZ) =>
        _viewers.TryGetValue((chunkX, chunkZ), out var count) ? count : 0;
}
