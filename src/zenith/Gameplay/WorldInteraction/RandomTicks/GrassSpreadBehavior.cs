using Zenith.Gameplay.Policy;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.RandomTicks;

/// <summary>
/// Grass turns a nearby dirt block into grass: one candidate per tick, within one block sideways and from three below
/// to one above, and only when the dirt has open space over it and that space is bright enough (<see cref="SpreadLight"/>, so
/// not at night and not in a cave). Grass with an opaque block on top of it turns back into dirt.
/// </summary>
sealed class GrassSpreadBehavior : IRandomTickBehavior
{
    private const int ReachSideways = 1;
    private const int ReachDown = 3;
    private const int ReachUp = 1;

    /// <summary>Brightness the cell over a dirt block needs for grass to take it.</summary>
    internal const int SpreadLight = 9;

    public void OnRandomTick(RandomTickContext tick)
    {
        if (tick.TryGetBlock(tick.X, tick.Y + 1, tick.Z, out var cover) && LightModel.FilterOf(cover) >= LightModel.Max)
        {
            tick.TrySet(EnvironmentCause.Decay, tick.X, tick.Y, tick.Z, Blocks.Dirt);
            return;
        }

        var x = tick.X + tick.Random.Next(-ReachSideways, ReachSideways + 1);
        var z = tick.Z + tick.Random.Next(-ReachSideways, ReachSideways + 1);
        var y = tick.Y + tick.Random.Next(-ReachDown, ReachUp + 1);

        if (!tick.TryGetBlock(x, y, z, out var target) || target != Blocks.Dirt) return;
        if (!tick.TryGetBlock(x, y + 1, z, out var above) || !IsOpen(above)) return;
        if (tick.BrightnessAt(x, y + 1, z) < SpreadLight) return;
        tick.TrySet(EnvironmentCause.Spread, x, y, z, Blocks.GrassBlock);
    }

    /// <summary>Space the sky (or a plant) can occupy: not a solid block, and not water.</summary>
    private static bool IsOpen(int blockRuntimeId) =>
        Blocks.IsAir(blockRuntimeId) || Blocks.IsPassableDecoration(blockRuntimeId);
}
