namespace Zenith.World;

enum Axis : byte { X, Y, Z }

enum VerticalHalf : byte { Bottom, Top }

enum TorchFacing : byte { Unknown, West, East, North, South, Top }

enum WallConnection : byte { None, Short, Tall }

/// <summary>
/// Every block-state property the server reads or writes, with the Bedrock wire spelling of its values. This is the
/// single place that knows property names; shapes use the typed fields.
/// </summary>
static class StateKeys
{
    // Facings. Stairs and trapdoors encode a direction as an int: east 0, west 1, south 2, north 3 (enum order N,E,S,W).
    public static readonly EnumKey<Cardinal> CardinalDirection = new("minecraft:cardinal_direction", "north", "east", "south", "west");
    public static readonly EnumKey<Cardinal> StairFacing = new("weirdo_direction", "3", "0", "2", "1");
    public static readonly EnumKey<Cardinal> TrapdoorFacing = new("direction", "3", "0", "2", "1");
    public static readonly EnumKey<TorchFacing> TorchFacing = new("torch_facing_direction", "unknown", "west", "east", "north", "south", "top");
    public static readonly EnumKey<Axis> PillarAxis = new("pillar_axis", "x", "y", "z");
    public static readonly EnumKey<VerticalHalf> VerticalHalf = new("minecraft:vertical_half", "bottom", "top");

    // Walls: one connection per side, indexed by <see cref="Cardinal"/>.
    public static readonly EnumKey<WallConnection>[] WallConnections =
    [
        new("wall_connection_type_north", "none", "short", "tall"),
        new("wall_connection_type_east", "none", "short", "tall"),
        new("wall_connection_type_south", "none", "short", "tall"),
        new("wall_connection_type_west", "none", "short", "tall")
    ];

    public static readonly BitKey WallPost = new("wall_post_bit");
    public static readonly BitKey Open = new("open_bit");
    public static readonly BitKey UpperBlock = new("upper_block_bit");
    public static readonly BitKey DoorHinge = new("door_hinge_bit");
    public static readonly BitKey UpsideDown = new("upside_down_bit");
    public static readonly BitKey InWall = new("in_wall_bit");
    public static readonly BitKey Age = new("age_bit");
    /// <summary>Water level: 0 is a source, 1..7 flows thinner with distance, +8 marks water that is falling.</summary>
    public static readonly IntKey LiquidDepth = new("liquid_depth", 16);
    public static readonly BitKey Persistent = new("persistent_bit");

    // Recognised for classification only.
    public static readonly PresenceKey StairCorner = new("minecraft:corner");
    public static readonly PresenceKey Deprecated = new("deprecated");
}
