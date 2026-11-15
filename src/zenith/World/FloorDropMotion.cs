namespace Zenith.World;

/// <summary>Item collision feet and velocity in blocks/tick, distinct from its custody-cell key.</summary>
readonly record struct FloorDropMotion(float X, float Y, float Z, float VelocityX, float VelocityY, float VelocityZ, bool OnGround = false)
{
    public static FloorDropMotion FromPlayer(Player.Player player)
    {
        var yaw = player.Yaw * (MathF.PI / 180f);
        var pitch = Math.Clamp(player.Pitch, -90f, 90f) * (MathF.PI / 180f);
        var dx = -MathF.Sin(yaw) * MathF.Cos(pitch);
        var dz = MathF.Cos(yaw) * MathF.Cos(pitch);
        return new(player.PositionX + dx * 0.3f, player.PositionY + 1.3f,
            player.PositionZ + dz * 0.3f, dx * 0.4f, -MathF.Sin(pitch) * 0.4f, dz * 0.4f);
    }
}
