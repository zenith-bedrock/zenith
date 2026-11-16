namespace Zenith.World;

/// <summary>Doors, trapdoors and fence gates: one <c>open</c> bit that a right click flips.</summary>
abstract class OpenableShape : BlockShape
{
    public override bool IsInteractive => true;

    public override bool TryInteract(World world, int x, int y, int z, int runtimeId, List<BlockWrite> writes)
    {
        // Iron variants answer to redstone only.
        if (States.FamilyOf(runtimeId) is not { } family || (family.Traits & BlockTraits.RedstoneOnly) != 0) return false;
        if (!TryToggle(ref runtimeId, out var open)) return false;
        writes.Add(new BlockWrite(x, y, z, runtimeId));
        OnToggled(world, x, y, z, runtimeId, open, writes);
        return true;
    }

    /// <summary>Called after the clicked half toggled; a door flips its other half here.</summary>
    protected virtual void OnToggled(World world, int x, int y, int z, int toggledId, bool open, List<BlockWrite> writes)
    {
    }

    protected static bool TryToggle(ref int runtimeId, out bool open)
    {
        open = !States.Bit(runtimeId, StateKeys.Open);
        return States.TrySet(ref runtimeId, StateKeys.Open, open);
    }
}

sealed class TrapdoorShape : OpenableShape
{
    public override IReadOnlyList<StateKey> RequiredKeys => [StateKeys.Open, StateKeys.UpsideDown, StateKeys.TrapdoorFacing];

    public override bool TryResolve(ref int runtimeId, in PlaceContext context) =>
        States.TrySet(ref runtimeId, StateKeys.TrapdoorFacing, context.Look.Opposite()) &&
        States.TrySet(ref runtimeId, StateKeys.UpsideDown, context.UpperHalf) &&
        States.TrySet(ref runtimeId, StateKeys.Open, false);
}

sealed class FenceGateShape : OpenableShape
{
    public override IReadOnlyList<StateKey> RequiredKeys => [StateKeys.Open, StateKeys.CardinalDirection];

    public override bool TryResolve(ref int runtimeId, in PlaceContext context) =>
        States.TrySet(ref runtimeId, StateKeys.CardinalDirection, context.Look) &&
        States.TrySet(ref runtimeId, StateKeys.Open, false);
}

/// <summary>Two blocks tall: the lower half is placed, the upper half follows it, and they open and break together.</summary>
sealed class DoorShape : OpenableShape
{
    public override IReadOnlyList<StateKey> RequiredKeys => [StateKeys.Open, StateKeys.UpperBlock, StateKeys.CardinalDirection];

    public override bool TryResolve(ref int runtimeId, in PlaceContext context) =>
        // The cardinal of a door is the player's look turned clockwise (a door facing north is encoded "east").
        States.TrySet(ref runtimeId, StateKeys.CardinalDirection, context.Look.Clockwise()) &&
        States.TrySet(ref runtimeId, StateKeys.UpperBlock, false) &&
        States.TrySet(ref runtimeId, StateKeys.Open, false);

    // The lower half stands on solid ground; the upper half needs its lower half.
    public override bool CanExist(World world, int x, int y, int z, int runtimeId)
    {
        if (!States.Bit(runtimeId, StateKeys.UpperBlock)) return IsSolid(world.GetBlock(x, y - 1, z));
        var below = world.GetBlock(x, y - 1, z);
        return SameFamily(runtimeId, below) && !States.Bit(below, StateKeys.UpperBlock);
    }

    public override bool HasRoom(World world, int x, int y, int z, int runtimeId) =>
        world.GetBlock(x, y + 1, z) == World.AirRuntimeId;

    public override void OnPlaced(World world, int x, int y, int z, int runtimeId, List<BlockWrite> extra)
    {
        if (States.TrySet(ref runtimeId, StateKeys.UpperBlock, true))
            extra.Add(new BlockWrite(x, y + 1, z, runtimeId));
    }

    public override void OnRemoved(World world, int x, int y, int z, int runtimeId, List<BlockWrite> extra)
    {
        // Breaking either half takes the whole door with it.
        var dy = States.Bit(runtimeId, StateKeys.UpperBlock) ? -1 : 1;
        if (SameFamily(runtimeId, world.GetBlock(x, y + dy, z)))
            extra.Add(new BlockWrite(x, y + dy, z, World.AirRuntimeId));
    }

    protected override void OnToggled(World world, int x, int y, int z, int toggledId, bool open, List<BlockWrite> writes)
    {
        var dy = States.Bit(toggledId, StateKeys.UpperBlock) ? -1 : 1;
        var partner = world.GetBlock(x, y + dy, z);
        if (SameFamily(toggledId, partner) && States.TrySet(ref partner, StateKeys.Open, open))
            writes.Add(new BlockWrite(x, y + dy, z, partner));
    }

    public override bool YieldsDrop(int runtimeId) => !States.Bit(runtimeId, StateKeys.UpperBlock);
}
