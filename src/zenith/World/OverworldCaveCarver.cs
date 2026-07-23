namespace Zenith.World;

/// <summary>
/// Deterministic worm + cheese cave carving for noise overworld (ADR §65).
/// </summary>
static class OverworldCaveCarver
{
    internal const int SurfaceGuardDepth = 4;

    private const int WormSalt = unchecked((int)0xC0FFEE42u);
    private const int WormInitSalt = unchecked((int)0x57304D01u);
    private const int CheeseSalt = unchecked((int)0xCEEE5E01u);

    private const int MinWormsPerChunk = 2;
    private const int MaxWormsPerChunk = 4;
    private const int MinWormLength = 48;
    private const int MaxWormLength = 128;

    public static bool IsCarved(int worldX, int worldY, int worldZ, int seed, int surfaceY)
    {
        using var ctx = OverworldCaveContext.ForColumn(FloorDiv(worldX, 16), FloorDiv(worldZ, 16), seed);
        return ctx.IsCarved(worldX, worldY, worldZ, surfaceY);
    }

    internal static void CollectSegments(int chunkX, int chunkZ, int seed, List<CaveSegment> into)
    {
        var h = Hash(chunkX, chunkZ, seed ^ WormSalt);
        var wormCount = MinWormsPerChunk + (int)(h % (uint)(MaxWormsPerChunk - MinWormsPerChunk + 1));

        for (var wi = 0; wi < wormCount; wi++)
            CollectWormSegments(chunkX, chunkZ, wi, seed, into);

        CollectCheeseSegment(chunkX, chunkZ, seed, into);
    }

    internal static bool IsWithinSegment(
        int px, int py, int pz,
        int x0, int y0, int z0,
        int x1, int y1, int z1,
        int radius)
    {
        var radiusSq = radius * radius;
        var dx = x1 - x0;
        var dy = y1 - y0;
        var dz = z1 - z0;
        var lenSq = dx * dx + dy * dy + dz * dz;

        if (lenSq == 0)
            return DistanceSquared(px, py, pz, x0, y0, z0) <= radiusSq;

        var ex = px - x0;
        var ey = py - y0;
        var ez = pz - z0;
        var dot = ex * dx + ey * dy + ez * dz;

        if (dot <= 0)
            return DistanceSquared(px, py, pz, x0, y0, z0) <= radiusSq;

        if (dot >= lenSq)
            return DistanceSquared(px, py, pz, x1, y1, z1) <= radiusSq;

        var cx = x0 + (dx * dot) / lenSq;
        var cy = y0 + (dy * dot) / lenSq;
        var cz = z0 + (dz * dot) / lenSq;
        return DistanceSquared(px, py, pz, cx, cy, cz) <= radiusSq;
    }

    private static void CollectWormSegments(
        int chunkX,
        int chunkZ,
        int wormIndex,
        int seed,
        List<CaveSegment> into)
    {
        var wh = Hash3(chunkX, wormIndex, chunkZ, seed ^ WormInitSalt);
        var baseX = chunkX * 16;
        var baseZ = chunkZ * 16;

        var x = baseX + (int)(wh % 16);
        var z = baseZ + (int)((wh >> 4) % 16);
        var y = 12 + (int)((wh >> 8) % 56);

        var dirX = (int)(wh >> 16) % 3 - 1;
        var dirZ = (int)(wh >> 18) % 3 - 1;
        if (dirX == 0 && dirZ == 0) dirX = 1;

        var length = MinWormLength + (int)((wh >> 20) % (uint)(MaxWormLength - MinWormLength + 1));

        for (var step = 0; step < length; step++)
        {
            var nextX = x + dirX;
            var nextY = y;
            if (step % 5 == 0 && step > 0)
                nextY += ((wh >> (step % 16)) & 1) == 0 ? -1 : 1;
            var nextZ = z + dirZ;

            if (step % 4 == 0)
            {
                var turn = Hash3(x, y, z, seed ^ unchecked((int)(step * 131u)));
                dirX = (int)(turn % 3) - 1;
                dirZ = (int)((turn >> 2) % 3) - 1;
                if (dirX == 0 && dirZ == 0) dirX = 1;
            }

            nextY = Math.Clamp(nextY, Blocks.FlatMinY + 2, 96);
            var radius = RadiusForDepth(y);
            into.Add(new CaveSegment(x, y, z, nextX, nextY, nextZ, radius));

            x = nextX;
            y = nextY;
            z = nextZ;
        }
    }

    private static void CollectCheeseSegment(int chunkX, int chunkZ, int seed, List<CaveSegment> into)
    {
        var h = Hash(chunkX, chunkZ, seed ^ CheeseSalt);
        if ((h % 9) != 0)
            return;

        var cx = chunkX * 16 + (int)((h >> 8) % 16);
        var cz = chunkZ * 16 + (int)((h >> 12) % 16);
        var cy = -20 + (int)((h >> 16) % 40);
        if (cy >= 0) cy = -8 - (int)(h % 16);

        var radius = 4 + (int)((h >> 20) % 3);
        into.Add(new CaveSegment(cx, cy, cz, cx, cy, cz, radius));
    }

    private static int RadiusForDepth(int worldY)
    {
        if (worldY >= 48) return 1;
        if (worldY >= 16) return 2;
        if (worldY >= 0) return 2;
        return 3;
    }

    private static int DistanceSquared(int ax, int ay, int az, int bx, int by, int bz)
    {
        var dx = ax - bx;
        var dy = ay - by;
        var dz = az - bz;
        return dx * dx + dy * dy + dz * dz;
    }

    private static int FloorDiv(int value, int divisor)
    {
        if (value >= 0) return value / divisor;
        return (value - (divisor - 1)) / divisor;
    }

    private static uint Hash(int x, int z, int seed)
    {
        unchecked
        {
            var h = (uint)seed;
            h ^= (uint)x * 374761393u;
            h ^= (uint)z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h;
        }
    }

    private static uint Hash3(int x, int y, int z, int seed)
    {
        unchecked
        {
            var h = (uint)seed;
            h ^= (uint)x * 374761393u;
            h ^= (uint)y * 668265263u;
            h ^= (uint)z * 2147483647u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h;
        }
    }
}
