using Zenith.Player;

namespace Zenith.Gameplay;

/// <summary>
/// Gameplay-owned decision for whether a player may receive an actor projection at a world
/// position. It reuses the chunk stream's confirmed-column state; it neither owns actors nor
/// creates packets.
/// </summary>
static class ActorInterest
{
    public static bool Includes(Player.Player player, float x, float z)
    {
        if (!player.IsInGame || player.IsDead) return false;
        // A negative radius is the explicit synthetic/unbounded view used by runtime fixtures.
        if (player.Chunks.Radius < 0) return true;
        return player.Chunks.Knows(PlayerChunkTracker.BlockToChunk(x), PlayerChunkTracker.BlockToChunk(z));
    }
}
