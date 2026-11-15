using Zenith.Geometry;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>Small item-body step: air drag/gravity and swept full-cube collision (ADR §163).</summary>
static class FloorDropPhysics
{
    public static FloorDropMotion Launch(World.World world, Player.Player player)
    {
        var launch = FloorDropMotion.FromPlayer(player);
        var box = Box(launch);
        for (var x = (int)MathF.Floor(box.MinX); x < MathF.Ceiling(box.MaxX); x++)
        for (var y = (int)MathF.Floor(box.MinY); y < MathF.Ceiling(box.MaxY); y++)
        for (var z = (int)MathF.Floor(box.MinZ); z < MathF.Ceiling(box.MaxZ); z++)
            if (Blocks.BlocksMovement(world.GetBlock(x, y, z)))
                return launch with { X = player.PositionX, Z = player.PositionZ };
        return launch;
    }

    public static FloorDropMotion Step(World.World world, FloorDropMotion body)
    {
        var vx = body.VelocityX * .98f;
        var vy = body.VelocityY * .98f - .04f;
        var vz = body.VelocityZ * .98f;
        var dy = Clip(world, Box(body), vy, 1);
        var ground = vy < 0 && MathF.Abs(dy - vy) > .000001f;
        body = body with { Y = body.Y + dy };
        var dx = Clip(world, Box(body), vx, 0);
        body = body with { X = body.X + dx };
        var dz = Clip(world, Box(body), vz, 2);
        body = body with { Z = body.Z + dz };
        if (MathF.Abs(dx - vx) > .000001f) vx = 0;
        if (MathF.Abs(dy - vy) > .000001f) vy = 0;
        if (MathF.Abs(dz - vz) > .000001f) vz = 0;
        if (ground) { vx *= .6f; vz *= .6f; }
        if (MathF.Abs(vx) < .001f) vx = 0;
        if (MathF.Abs(vy) < .001f) vy = 0;
        if (MathF.Abs(vz) < .001f) vz = 0;
        return body with { VelocityX = vx, VelocityY = vy, VelocityZ = vz, OnGround = ground };
    }

    private static Aabb Box(FloorDropMotion body) =>
        Aabb.FromCenterSize(body.X, body.Y, body.Z, EntityHitboxes.ItemEntitySize, EntityHitboxes.ItemEntitySize);

    private static float Clip(World.World world, Aabb box, float delta, int axis)
    {
        if (delta == 0) return 0;
        var dx = axis == 0 ? delta : 0; var dy = axis == 1 ? delta : 0; var dz = axis == 2 ? delta : 0;
        var minX = (int)MathF.Floor(MathF.Min(box.MinX, box.MinX + dx));
        var minY = (int)MathF.Floor(MathF.Min(box.MinY, box.MinY + dy));
        var minZ = (int)MathF.Floor(MathF.Min(box.MinZ, box.MinZ + dz));
        var maxX = (int)MathF.Ceiling(MathF.Max(box.MaxX, box.MaxX + dx));
        var maxY = (int)MathF.Ceiling(MathF.Max(box.MaxY, box.MaxY + dy));
        var maxZ = (int)MathF.Ceiling(MathF.Max(box.MaxZ, box.MaxZ + dz));
        for (var x = minX; x < maxX; x++)
        for (var y = minY; y < maxY; y++)
        for (var z = minZ; z < maxZ; z++)
        {
            if (!Blocks.BlocksMovement(world.GetBlock(x, y, z))) continue;
            if (axis != 0 && (box.MaxX <= x || box.MinX >= x + 1)) continue;
            if (axis != 1 && (box.MaxY <= y || box.MinY >= y + 1)) continue;
            if (axis != 2 && (box.MaxZ <= z || box.MinZ >= z + 1)) continue;
            var min = axis == 0 ? box.MinX : axis == 1 ? box.MinY : box.MinZ;
            var max = axis == 0 ? box.MaxX : axis == 1 ? box.MaxY : box.MaxZ;
            var cell = axis == 0 ? x : axis == 1 ? y : z;
            if (delta > 0 && max <= cell) delta = MathF.Min(delta, cell - max);
            if (delta < 0 && min >= cell + 1) delta = MathF.Max(delta, cell + 1 - min);
        }
        return delta;
    }
}
