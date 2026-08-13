namespace Zenith.Gameplay;

/// <summary>
/// Phase XV extraction: the bounded local movement-validity probe (two empty body cells, one
/// supporting cell — no pathfinding) turned out identical across Zombie's chase, Cow's wander, and
/// Creeper's approach once Creeper needed to walk toward a target. Three real consumers, not two —
/// the threshold Phase XIV.1 named for this exact check. Deliberately still just a validity
/// predicate: it does not move anything, decide a heading, or know what a "mob" is beyond a
/// position and a world. Each caller still owns choosing a destination and assigning it.
/// </summary>
static class GroundMobMovement
{
    public static bool CanStandAt(World.World world, float x, float y, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        return world.GetBlock(blockX, blockY, blockZ) == World.World.AirRuntimeId &&
               world.GetBlock(blockX, blockY + 1, blockZ) == World.World.AirRuntimeId &&
               world.GetBlock(blockX, blockY - 1, blockZ) != World.World.AirRuntimeId;
    }
}
