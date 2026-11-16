using Zenith.World;

namespace Zenith.Tests;

internal static class WorldTestExtensions
{
    /// <summary>
    /// A write that tells nobody: not the neighbours, not gravity. For tests that stage sand and gravel by hand and then drive gravity
    /// themselves; an ordinary write now makes gravity notice the block.
    /// </summary>
    public static bool SetQuietly(this World.World world, int x, int y, int z, int blockRuntimeId) =>
        world.TrySetBlock(x, y, z, blockRuntimeId, SetBlockFlags.None);
}
