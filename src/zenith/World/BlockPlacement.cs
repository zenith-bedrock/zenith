namespace Zenith.World;

/// <summary>
/// Decides the block state the server places: the held block's own state with only the properties the placement
/// context determines (face, cursor height, player yaw) overridden. Driven by <see cref="BlockKind"/>, so every wood,
/// stone or colour variant of a family is handled without a per-name rule.
/// </summary>
static class BlockPlacement
{
    private const string VerticalHalf = "minecraft:vertical_half";

    /// <summary>
    /// Resolves the runtime id to place for <paramref name="heldRuntimeId"/> against <paramref name="face"/>
    /// (0 down … 5 east) with the cursor at <paramref name="clickY"/> (0..1 inside the clicked block).
    /// False when the block has no valid placement for this context (e.g. a torch on a ceiling).
    /// </summary>
    public static bool TryResolve(int heldRuntimeId, byte face, float clickY, float yawDegrees, out int placedRuntimeId)
    {
        placedRuntimeId = heldRuntimeId;
        var catalog = Blocks.Catalog;
        var kind = catalog.KindOf(heldRuntimeId);
        if (kind == BlockKind.DoubleSlab) return false;
        // Curated placeables the catalog does not model (walls, crafting table) are placed exactly as held.
        if (kind == BlockKind.Unsupported) return Blocks.IsPlaceable(heldRuntimeId);
        if (kind is BlockKind.Cube or BlockKind.Plant or BlockKind.Wall) return true;

        if (!catalog.Palette.TryGetName(heldRuntimeId, out var name)) return false;
        var overrides = new Dictionary<string, string>(2);

        switch (kind)
        {
            case BlockKind.Pillar:
                overrides["pillar_axis"] = face switch { 0 or 1 => "y", 2 or 3 => "z", _ => "x" };
                break;

            case BlockKind.Stairs:
                overrides["weirdo_direction"] = StairDirection(PlaceFacing.LookFromYaw(yawDegrees));
                overrides["upside_down_bit"] = IsUpperHalf(face, clickY) ? "1" : "0";
                break;

            case BlockKind.Slab:
                overrides[VerticalHalf] = IsUpperHalf(face, clickY) ? "top" : "bottom";
                break;

            case BlockKind.Door:
                // PocketMine encodes a door facing north as "east": the cardinal is the player's look turned clockwise.
                overrides["minecraft:cardinal_direction"] = Clockwise(PlaceFacing.LookFromYaw(yawDegrees));
                overrides["upper_block_bit"] = "0";
                overrides["open_bit"] = "0";
                break;

            case BlockKind.Trapdoor:
                overrides["direction"] = TrapdoorDirection(PlaceFacing.Opposite(PlaceFacing.LookFromYaw(yawDegrees)));
                overrides["upside_down_bit"] = IsUpperHalf(face, clickY) ? "1" : "0";
                overrides["open_bit"] = "0";
                break;

            case BlockKind.FenceGate:
                overrides["minecraft:cardinal_direction"] = PlaceFacing.LookFromYaw(yawDegrees);
                overrides["open_bit"] = "0";
                break;

            case BlockKind.Torch:
                if (face == 0) return false;
                overrides["torch_facing_direction"] = face switch
                {
                    1 => "top",
                    2 => "north",
                    3 => "south",
                    4 => "west",
                    _ => "east"
                };
                break;
        }

        // Start from the held block's own state so any property we do not decide keeps the item's value.
        if (!catalog.Palette.TryGetState(heldRuntimeId, out var held)) return false;
        var merged = held.With(overrides);
        return merged is not null && catalog.Palette.TryGetExact(name, merged, out placedRuntimeId);
    }

    /// <summary>
    /// Whether <paramref name="runtimeId"/> may stand at the cell: plants need ground (soil for most), everything else
    /// is unconstrained. Reads the world so a placement against thin air is refused instead of floating.
    /// </summary>
    public static bool HasSupport(World world, int x, int y, int z, int runtimeId)
    {
        var catalog = Blocks.Catalog;
        if (catalog.KindOf(runtimeId) == BlockKind.Door)
        {
            // Two blocks tall: solid ground below and room for the upper half.
            var ground = world.GetBlock(x, y - 1, z);
            return ground != World.AirRuntimeId && !Blocks.IsFluid(ground) && !Blocks.IsPassableDecoration(ground) &&
                   world.GetBlock(x, y + 1, z) == World.AirRuntimeId;
        }
        if (catalog.KindOf(runtimeId) != BlockKind.Plant) return true;
        if (!catalog.Palette.TryGetName(runtimeId, out var name)) return false;

        var below = world.GetBlock(x, y - 1, z);
        if (below == World.AirRuntimeId || Blocks.IsFluid(below) || Blocks.IsPassableDecoration(below)) return false;
        if (!BlockSupportRules.NeedsSoil(name)) return true;
        return catalog.Palette.TryGetName(below, out var belowName) && BlockSupportRules.IsSoil(belowName);
    }

    // Slab/stair half: the bottom face always yields the upper half, the top face the lower half, and a side face
    // follows which half of the wall the cursor touched.
    private static bool IsUpperHalf(byte face, float clickY) => face switch
    {
        0 => true,
        1 => false,
        _ => clickY > 0.5f
    };

    internal static string Clockwise(string cardinal) => cardinal switch
    {
        Blocks.CardinalNorth => Blocks.CardinalEast,
        Blocks.CardinalEast => Blocks.CardinalSouth,
        Blocks.CardinalSouth => Blocks.CardinalWest,
        _ => Blocks.CardinalNorth
    };

    // PocketMine's write5MinusHorizontalFacing: trapdoors use east 0, west 1, south 2, north 3 (not the legacy order).
    private static string TrapdoorDirection(string cardinal) => cardinal switch
    {
        Blocks.CardinalEast => "0",
        Blocks.CardinalWest => "1",
        Blocks.CardinalSouth => "2",
        _ => "3"
    };

    // Bedrock weirdo_direction: 0 east, 1 west, 2 south, 3 north — the direction the player is looking.
    private static string StairDirection(string cardinal) => cardinal switch
    {
        Blocks.CardinalEast => "0",
        Blocks.CardinalWest => "1",
        Blocks.CardinalSouth => "2",
        _ => "3"
    };
}
