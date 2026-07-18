namespace Zenith.World;

/// <summary>
/// Yaw → Minecraft cardinal helpers for place remaps (chest today; other directional
/// blocks reuse this without a BlockBehavior framework — ADR §46).
/// </summary>
static class PlaceFacing
{
    /// <summary>Horizontal look direction from yaw (0 = south, 90 = west, …).</summary>
    public static string LookFromYaw(float yawDegrees)
    {
        var yaw = yawDegrees % 360f;
        if (yaw < 0f) yaw += 360f;

        return yaw switch
        {
            >= 45f and < 135f => Blocks.CardinalWest,
            >= 135f and < 225f => Blocks.CardinalNorth,
            >= 225f and < 315f => Blocks.CardinalEast,
            _ => Blocks.CardinalSouth
        };
    }

    public static string Opposite(string cardinal) => cardinal switch
    {
        Blocks.CardinalNorth => Blocks.CardinalSouth,
        Blocks.CardinalSouth => Blocks.CardinalNorth,
        Blocks.CardinalEast => Blocks.CardinalWest,
        Blocks.CardinalWest => Blocks.CardinalEast,
        _ => Blocks.CardinalSouth
    };

    /// <summary>Block front faces the player = opposite of horizontal look.</summary>
    public static string FrontTowardPlayerFromYaw(float yawDegrees) =>
        Opposite(LookFromYaw(yawDegrees));
}
