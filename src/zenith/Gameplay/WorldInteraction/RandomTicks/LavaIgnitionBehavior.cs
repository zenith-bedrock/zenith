using Zenith.Gameplay.Policy;
using Zenith.Gameplay.WorldInteraction.Fire;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.RandomTicks;

/// <summary>
/// Lava sets fire to what is above and around it (ADR §172). Most ticks it reaches up to two cells out and one up and lights
/// the first open cell that touches something burnable; otherwise it looks at three cells beside itself and lights the air
/// above any that burns. A solid block in the way ends the search.
/// </summary>
sealed class LavaIgnitionBehavior : IRandomTickBehavior
{
    public void OnRandomTick(RandomTickContext tick)
    {
        var random = tick.Random;
        var reach = random.Next(3);
        if (reach > 0)
        {
            var (x, y, z) = (tick.X, tick.Y, tick.Z);
            for (var i = 0; i < reach; i++)
            {
                (x, y, z) = (x + random.Next(3) - 1, y + 1, z + random.Next(3) - 1);
                if (!tick.TryGetBlock(x, y, z, out var id)) return;
                if (Blocks.IsAir(id))
                {
                    if (HasFlammableNeighbour(tick, x, y, z) && tick.Allows(EnvironmentCause.Fire, x, y, z, id, FireBlocks.WithAge(0)))
                        tick.TrySet(EnvironmentCause.Fire, x, y, z, FireBlocks.WithAge(0));
                    return;
                }

                if (Blocks.BlocksMovement(id)) return;
            }

            return;
        }

        for (var i = 0; i < 3; i++)
        {
            var (x, z) = (tick.X + random.Next(3) - 1, tick.Z + random.Next(3) - 1);
            if (!tick.TryGetBlock(x, tick.Y + 1, z, out var above) || !Blocks.IsAir(above)) continue;
            if (tick.TryGetBlock(x, tick.Y, z, out var beside) && Blocks.FlammabilityOf(beside) > 0)
            {
                if (!tick.Allows(EnvironmentCause.Fire, x, tick.Y + 1, z, above, FireBlocks.WithAge(0))) return;
                tick.TrySet(EnvironmentCause.Fire, x, tick.Y + 1, z, FireBlocks.WithAge(0));
                return;
            }
        }
    }

    private static bool HasFlammableNeighbour(RandomTickContext tick, int x, int y, int z)
    {
        foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
            if (tick.TryGetBlock(x + dx, y + dy, z + dz, out var id) && Blocks.FlammabilityOf(id) > 0)
                return true;
        return false;
    }
}
