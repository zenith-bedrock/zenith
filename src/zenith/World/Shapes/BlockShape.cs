namespace Zenith.World;

/// <summary>One cell write produced by a shape rule; the gameplay tick applies it.</summary>
readonly record struct BlockWrite(int X, int Y, int Z, int BlockRuntimeId);

/// <summary>What the player did when placing: the clicked face, where on it the cursor was, and which way they look.</summary>
readonly record struct PlaceContext(Face Face, float CursorY, float YawDegrees)
{
    /// <summary>The horizontal direction the player is looking.</summary>
    public Cardinal Look => Directions.LookFromYaw(YawDegrees);

    /// <summary>
    /// Slab/stair/trapdoor half: the bottom face of a block always yields the upper half, the top face the lower
    /// half, and a side face follows which half of the wall the cursor touched.
    /// </summary>
    public bool UpperHalf => Face switch
    {
        Face.Down => true,
        Face.Up => false,
        _ => CursorY > 0.5f
    };
}

/// <summary>
/// The rules of one <see cref="BlockKind"/>: how a held block becomes the placed state, whether it can stand where it
/// is, how it reshapes when its surroundings change and what happens when it is clicked. Shapes are stateless and
/// world-reading; they never write — they report <see cref="BlockWrite"/>s and the gameplay tick commits them, so the
/// single-writer rule holds. All defaults describe a plain cube.
/// </summary>
abstract class BlockShape
{
    protected static BlockStateTable States => Blocks.Catalog.States;

    /// <summary>Right click toggles or opens it (doors, trapdoors, fence gates).</summary>
    public virtual bool IsInteractive => false;

    /// <summary>Chooses the state to place from the held block and the placement context; false when it cannot be placed that way.</summary>
    public virtual bool TryResolve(ref int runtimeId, in PlaceContext context) => true;

    /// <summary>Whether the block can stand at the cell (ground for plants, the wall a torch hangs on…). Re-checked on every neighbour change.</summary>
    public virtual bool CanExist(World world, int x, int y, int z, int runtimeId) => true;

    /// <summary>Whether there is room for the block beyond its own cell (a door needs headroom).</summary>
    public virtual bool HasRoom(World world, int x, int y, int z, int runtimeId) => true;

    /// <summary>Extra writes a placement implies (the upper half of a door).</summary>
    public virtual void OnPlaced(World world, int x, int y, int z, int runtimeId, List<BlockWrite> extra)
    {
    }

    /// <summary>Extra writes a removal implies (the other half of a door).</summary>
    public virtual void OnRemoved(World world, int x, int y, int z, int runtimeId, List<BlockWrite> extra)
    {
    }

    /// <summary>The state the block should have given its neighbours (wall connections); the same id when nothing changes.</summary>
    public virtual int UpdateShape(World world, int x, int y, int z, int runtimeId) => runtimeId;

    /// <summary>Right click; true when it produced <paramref name="writes"/>.</summary>
    public virtual bool TryInteract(World world, int x, int y, int z, int runtimeId, List<BlockWrite> writes) => false;

    /// <summary>False for a half that breaks along with the half that carries the item.</summary>
    public virtual bool YieldsDrop(int runtimeId) => true;

    /// <summary>A solid block the world would not let an actor walk through, below or beside it.</summary>
    protected static bool IsSolid(int runtimeId) => Blocks.BlocksMovement(runtimeId);

    protected static bool SameFamily(int a, int b) =>
        States.FamilyOf(a) is { } family && ReferenceEquals(family, States.FamilyOf(b));
}
