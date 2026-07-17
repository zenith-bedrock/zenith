namespace Zenith.Geometry;

/// <summary>
/// Axis-aligned bounding box (pure math). No Minecraft sizes — see <c>World.EntityHitboxes</c>.
/// </summary>
readonly struct Aabb
{
    public float MinX { get; }
    public float MinY { get; }
    public float MinZ { get; }
    public float MaxX { get; }
    public float MaxY { get; }
    public float MaxZ { get; }

    public Aabb(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    public static Aabb FromMinMax(float minX, float minY, float minZ, float maxX, float maxY, float maxZ) =>
        new(minX, minY, minZ, maxX, maxY, maxZ);

    /// <summary>Box centered on <paramref name="cx"/>/<paramref name="cz"/>, bottom at <paramref name="feetY"/>.</summary>
    public static Aabb FromCenterSize(float cx, float feetY, float cz, float width, float height)
    {
        var half = width * 0.5f;
        return new Aabb(cx - half, feetY, cz - half, cx + half, feetY + height, cz + half);
    }

    public bool Intersects(in Aabb other) =>
        MinX < other.MaxX && MaxX > other.MinX &&
        MinY < other.MaxY && MaxY > other.MinY &&
        MinZ < other.MaxZ && MaxZ > other.MinZ;

    public bool Contains(float x, float y, float z) =>
        x >= MinX && x <= MaxX &&
        y >= MinY && y <= MaxY &&
        z >= MinZ && z <= MaxZ;

    /// <summary>Grows the box by <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/> on each side.</summary>
    public Aabb Expand(float x, float y, float z) =>
        new(MinX - x, MinY - y, MinZ - z, MaxX + x, MaxY + y, MaxZ + z);

    public Aabb Expand((float X, float Y, float Z) amount) =>
        Expand(amount.X, amount.Y, amount.Z);
}
