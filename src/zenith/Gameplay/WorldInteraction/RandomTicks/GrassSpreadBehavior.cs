using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.RandomTicks;

/// <summary>
/// Grass turns a nearby dirt block into grass: one candidate per tick, within one block sideways and from three below
/// to one above, and only when the dirt has open space over it. Decay (grass under an opaque block turning back into
/// dirt) is not here: it depends on light, and Zenith has no light model yet (ADR §169).
/// </summary>
sealed class GrassSpreadBehavior : IRandomTickBehavior
{
    private const int ReachSideways = 1;
    private const int ReachDown = 3;
    private const int ReachUp = 1;

    public void OnRandomTick(RandomTickContext tick)
    {
        var x = tick.X + tick.Random.Next(-ReachSideways, ReachSideways + 1);
        var z = tick.Z + tick.Random.Next(-ReachSideways, ReachSideways + 1);
        var y = tick.Y + tick.Random.Next(-ReachDown, ReachUp + 1);

        if (!tick.TryGetBlock(x, y, z, out var target) || target != Blocks.Dirt) return;
        if (!tick.TryGetBlock(x, y + 1, z, out var above) || !IsOpen(above)) return;
        tick.TrySet(x, y, z, Blocks.GrassBlock);
    }

    /// <summary>Space the sky (or a plant) can occupy: not a solid block, and not water.</summary>
    private static bool IsOpen(int blockRuntimeId) =>
        Blocks.IsAir(blockRuntimeId) || Blocks.IsPassableDecoration(blockRuntimeId);
}
