namespace Zenith.World;

/// <summary>
/// Place policy: chest front faces the player (cardinal opposite of horizontal look).
/// Yaw bins are Zenith’s locked convention — flip opposite mapping if LAN smoke shows backs.
/// </summary>
static class ChestFacing
{
    /// <summary>Returns <see cref="Blocks.CardinalNorth"/> / East / South / West.</summary>
    public static string FromYaw(float yawDegrees) =>
        PlaceFacing.FrontTowardPlayerFromYaw(yawDegrees);

    public static int RuntimeIdFromYaw(float yawDegrees) =>
        Blocks.ChestForFacing(FromYaw(yawDegrees));
}
