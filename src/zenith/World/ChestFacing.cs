namespace Zenith.World;

/// <summary>
/// Place policy: chest front faces the player (cardinal opposite of horizontal look).
/// Yaw bins are Zenith’s locked convention — flip opposite mapping if LAN smoke shows backs.
/// </summary>
static class ChestFacing
{
    /// <summary>Returns <see cref="Blocks.CardinalNorth"/> / East / South / West.</summary>
    public static string FromYaw(float yawDegrees)
    {
        var yaw = yawDegrees % 360f;
        if (yaw < 0f) yaw += 360f;

        // Player look direction (Minecraft-style: 0 = south, 90 = west, …).
        var look = yaw switch
        {
            >= 45f and < 135f => Blocks.CardinalWest,
            >= 135f and < 225f => Blocks.CardinalNorth,
            >= 225f and < 315f => Blocks.CardinalEast,
            _ => Blocks.CardinalSouth
        };

        // Front toward player = opposite of look.
        return look switch
        {
            Blocks.CardinalNorth => Blocks.CardinalSouth,
            Blocks.CardinalSouth => Blocks.CardinalNorth,
            Blocks.CardinalEast => Blocks.CardinalWest,
            _ => Blocks.CardinalEast
        };
    }

    public static int RuntimeIdFromYaw(float yawDegrees) =>
        Blocks.ChestForFacing(FromYaw(yawDegrees));
}
