namespace Zenith.World;

/// <summary>Blocks the catalog does not model (crafting table, chest…): placed exactly as held when curated as placeable.</summary>
sealed class CuratedShape : BlockShape
{
    public override bool TryResolve(ref int runtimeId, in PlaceContext context) => Blocks.IsPlaceable(runtimeId);
}

/// <summary>No server-decided state — placed as held.</summary>
sealed class CubeShape : BlockShape;

sealed class PillarShape : BlockShape
{
    public override bool TryResolve(ref int runtimeId, in PlaceContext context)
    {
        var axis = context.Face switch
        {
            Face.Down or Face.Up => Axis.Y,
            Face.North or Face.South => Axis.Z,
            _ => Axis.X
        };
        return States.TrySet(ref runtimeId, StateKeys.PillarAxis, axis);
    }
}

sealed class StairsShape : BlockShape
{
    public override bool TryResolve(ref int runtimeId, in PlaceContext context) =>
        States.TrySet(ref runtimeId, StateKeys.StairFacing, context.Look) &&
        States.TrySet(ref runtimeId, StateKeys.UpsideDown, context.UpperHalf);
}

/// <summary>Ground decoration: needs a solid block underneath, and soil for most species.</summary>
sealed class PlantShape : BlockShape
{
    public override bool CanExist(World world, int x, int y, int z, int runtimeId)
    {
        var below = world.GetBlock(x, y - 1, z);
        if (!IsSolid(below)) return false;
        var family = States.FamilyOf(runtimeId);
        if (family is null || (family.Traits & BlockTraits.NeedsSoil) == 0) return true;
        return States.FamilyOf(below) is { } ground && (ground.Traits & BlockTraits.Soil) != 0;
    }
}

/// <summary>Hangs on the block opposite its facing, or stands on the one below.</summary>
sealed class TorchShape : BlockShape
{
    public override bool TryResolve(ref int runtimeId, in PlaceContext context)
    {
        TorchFacing facing;
        switch (context.Face)
        {
            case Face.Down: return false; // no torch on a ceiling
            case Face.Up: facing = TorchFacing.Top; break;
            case Face.North: facing = TorchFacing.North; break;
            case Face.South: facing = TorchFacing.South; break;
            case Face.West: facing = TorchFacing.West; break;
            default: facing = TorchFacing.East; break;
        }
        return States.TrySet(ref runtimeId, StateKeys.TorchFacing, facing);
    }

    // A torch placed on the north face of a block sits one cell north of it and is "north": the support lies the other way.
    // Zenith's own placement convention, not yet checked against a rendered client.
    public override bool CanExist(World world, int x, int y, int z, int runtimeId)
    {
        if (!States.TryGet(runtimeId, StateKeys.TorchFacing, out TorchFacing facing)) return true;
        var (dx, dy, dz) = facing switch
        {
            TorchFacing.Top => (0, -1, 0),
            TorchFacing.North => (0, 0, 1),
            TorchFacing.South => (0, 0, -1),
            TorchFacing.West => (1, 0, 0),
            TorchFacing.East => (-1, 0, 0),
            _ => (0, 0, 0) // unknown facing: leave the torch alone rather than guess
        };
        return (dx | dy | dz) == 0 || IsSolid(world.GetBlock(x + dx, y + dy, z + dz));
    }
}
