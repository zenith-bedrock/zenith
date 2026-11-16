namespace Zenith.World;

/// <summary>
/// The one entry point to block rules. Each <see cref="BlockKind"/> has a <see cref="BlockShape"/> in <c>Shapes/</c>
/// that owns its placement, support, reshaping and interaction; this class only routes a runtime id to its shape and
/// holds the few rules that span two blocks (replacing air, slab merging). Everything here is read-only against the
/// world: the writes it reports are committed by the gameplay tick.
/// </summary>
static class BlockShapes
{
    private static readonly BlockShape[] ByKind = BuildTable();

    private static BlockShape[] BuildTable()
    {
        var table = new BlockShape[Enum.GetValues<BlockKind>().Length];
        Array.Fill(table, new CubeShape());
        table[(int)BlockKind.Unsupported] = new CuratedShape();
        table[(int)BlockKind.Pillar] = new PillarShape();
        table[(int)BlockKind.Stairs] = new StairsShape();
        table[(int)BlockKind.Slab] = new SlabShape();
        table[(int)BlockKind.DoubleSlab] = new DoubleSlabShape();
        table[(int)BlockKind.Plant] = new PlantShape();
        table[(int)BlockKind.Torch] = new TorchShape();
        table[(int)BlockKind.Door] = new DoorShape();
        table[(int)BlockKind.Trapdoor] = new TrapdoorShape();
        table[(int)BlockKind.FenceGate] = new FenceGateShape();
        table[(int)BlockKind.Wall] = new WallShape();
        return table;
    }

    public static BlockShape For(int runtimeId) => ByKind[(int)Blocks.Catalog.KindOf(runtimeId)];

    public static BlockShape ForKind(BlockKind kind) => ByKind[(int)kind];

    /// <summary>The state to place for a held block clicked on <paramref name="face"/> with the cursor at <paramref name="cursorY"/> (0..1).</summary>
    public static bool TryResolve(int heldRuntimeId, byte face, float cursorY, float yawDegrees, out int placedRuntimeId)
    {
        placedRuntimeId = heldRuntimeId;
        return For(heldRuntimeId).TryResolve(ref placedRuntimeId, new PlaceContext(Directions.ToFace(face), cursorY, yawDegrees));
    }

    /// <summary>Whether the block can stand at the cell given what is around it.</summary>
    public static bool CanExist(World world, int x, int y, int z, int runtimeId) =>
        For(runtimeId).CanExist(world, x, y, z, runtimeId);

    /// <summary>Whether <paramref name="runtimeId"/> may be written over <paramref name="existing"/>: air (with room for the block), or a slab merging.</summary>
    public static bool CanPlace(World world, int x, int y, int z, int runtimeId, int existing)
    {
        if (SlabShape.IsMerge(existing, runtimeId)) return true;
        // A double slab only ever comes from merging into a single slab, never into open air.
        if (existing != World.AirRuntimeId || Blocks.Catalog.KindOf(runtimeId) == BlockKind.DoubleSlab) return false;
        return For(runtimeId).HasRoom(world, x, y, z, runtimeId);
    }

    public static void OnPlaced(World world, int x, int y, int z, int runtimeId, List<BlockWrite> extra) =>
        For(runtimeId).OnPlaced(world, x, y, z, runtimeId, extra);

    public static void OnRemoved(World world, int x, int y, int z, int removedRuntimeId, List<BlockWrite> extra) =>
        For(removedRuntimeId).OnRemoved(world, x, y, z, removedRuntimeId, extra);

    /// <summary>The state the block at the cell should have given its neighbours; the same id when nothing changes.</summary>
    public static int UpdateShape(World world, int x, int y, int z, int runtimeId) =>
        For(runtimeId).UpdateShape(world, x, y, z, runtimeId);

    public static bool IsInteractive(int runtimeId) => For(runtimeId).IsInteractive;

    /// <summary>Right click on the block at the cell; true when it produced <paramref name="writes"/>.</summary>
    public static bool TryInteract(World world, int x, int y, int z, List<BlockWrite> writes)
    {
        var id = world.GetBlock(x, y, z);
        return For(id).TryInteract(world, x, y, z, id, writes);
    }

    public static bool YieldsDrop(int runtimeId) => For(runtimeId).YieldsDrop(runtimeId);

    /// <inheritdoc cref="SlabShape.TryMerge"/>
    public static bool TryMergeSlab(int existing, int placed, bool requireOppositeHalf, byte? clickedFace, out int doubleSlab) =>
        SlabShape.TryMerge(existing, placed, requireOppositeHalf, clickedFace is { } face ? Directions.ToFace(face) : null, out doubleSlab);

    public static bool IsSlabMerge(int existing, int placed) => SlabShape.IsMerge(existing, placed);
}
