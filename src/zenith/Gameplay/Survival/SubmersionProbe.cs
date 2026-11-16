using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>Which liquid a player's feet and head are in, read from the resident world (a column that is not loaded counts as open air).</summary>
static class SubmersionProbe
{
    /// <summary>A little above the sole so standing on a block under a liquid surface does not count as in it.</summary>
    private const float FeetOffset = 0.1f;

    public static FluidType AtFeet(Player.Player player) => FluidAt(player, FeetOffset);

    public static FluidType AtHead(Player.Player player) => FluidAt(player, Blocks.PlayerEyeHeight);

    /// <summary>Swimming is sprinting with the whole body in water, which is what makes the horizontal pose.</summary>
    public static bool IsSwimming(Player.Player player) =>
        player.IsSprinting && AtFeet(player) == FluidType.Water && AtHead(player) == FluidType.Water;

    private static FluidType FluidAt(Player.Player player, float heightAboveFeet)
    {
        var x = (int)MathF.Floor(player.PositionX);
        var y = (int)MathF.Floor(player.PositionY + heightAboveFeet);
        var z = (int)MathF.Floor(player.PositionZ);
        return player.Level.TryGetBlock(x, y, z, out var id) ? Blocks.FluidOf(id) : FluidType.None;
    }
}
