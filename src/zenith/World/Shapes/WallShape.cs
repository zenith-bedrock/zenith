namespace Zenith.World;

/// <summary>
/// Walls join walls, fence gates and full opaque blocks on their four sides — "tall" when something sits on top —
/// and are a post unless they run straight through. The shape is recomputed whenever a neighbour changes.
/// </summary>
sealed class WallShape : BlockShape
{
    private const int NorthSouth = 1 << (int)Cardinal.North | 1 << (int)Cardinal.South;
    private const int EastWest = 1 << (int)Cardinal.East | 1 << (int)Cardinal.West;

    public override int UpdateShape(World world, int x, int y, int z, int runtimeId)
    {
        var capped = IsSolid(world.GetBlock(x, y + 1, z));
        var connected = 0;
        for (var i = 0; i < 4; i++)
        {
            var side = (Cardinal)i;
            var joined = ConnectsTo(world.GetBlock(x + side.DeltaX(), y, z + side.DeltaZ()));
            if (joined) connected |= 1 << i;
            _ = States.TrySet(ref runtimeId, StateKeys.WallConnections[i],
                !joined ? WallConnection.None : capped ? WallConnection.Tall : WallConnection.Short);
        }

        var runsStraight = connected is NorthSouth or EastWest;
        _ = States.TrySet(ref runtimeId, StateKeys.WallPost, !(runsStraight && !capped));
        return runtimeId;
    }

    private static bool ConnectsTo(int runtimeId)
    {
        var family = States.FamilyOf(runtimeId);
        if (family is null) return false;
        if (family.Kind is BlockKind.Wall or BlockKind.FenceGate) return true;
        return (family.Traits & BlockTraits.WallConnectable) != 0 && IsSolid(runtimeId);
    }
}
