namespace Zenith.World;

/// <summary>
/// Precomputed worm segments for a column neighborhood (ADR §65 / §69).
/// Built once per noise column; XZ spatial grid for O(nearby) carve tests.
/// </summary>
readonly struct OverworldCaveContext
{
    internal const int CellSize = 8;
    internal const int NeighborhoodChunks = 3;
    internal const int NeighborhoodBlocks = NeighborhoodChunks * 16;
    internal const int Grid = NeighborhoodBlocks / CellSize; // 6
    internal const int BucketCount = Grid * Grid;

    private readonly CaveSegment[] _segments;
    private readonly int _originX;
    private readonly int _originZ;
    /// <summary>CSR: length <see cref="BucketCount"/> + 1.</summary>
    private readonly int[] _bucketOffsets;
    private readonly int[] _bucketIndices;

    private OverworldCaveContext(
        CaveSegment[] segments,
        int originX,
        int originZ,
        int[] bucketOffsets,
        int[] bucketIndices)
    {
        _segments = segments;
        _originX = originX;
        _originZ = originZ;
        _bucketOffsets = bucketOffsets;
        _bucketIndices = bucketIndices;
    }

    public static OverworldCaveContext ForColumn(int chunkX, int chunkZ, int seed)
    {
        var list = new List<CaveSegment>(4096);
        for (var dcx = -1; dcx <= 1; dcx++)
        {
            for (var dcz = -1; dcz <= 1; dcz++)
                OverworldCaveCarver.CollectSegments(chunkX + dcx, chunkZ + dcz, seed, list);
        }

        var segments = list.Count == 0 ? Array.Empty<CaveSegment>() : list.ToArray();
        var originX = (chunkX - 1) * 16;
        var originZ = (chunkZ - 1) * 16;
        BuildSpatialIndex(segments, originX, originZ, out var offsets, out var indices);
        return new OverworldCaveContext(segments, originX, originZ, offsets, indices);
    }

    public bool IsCarved(int worldX, int worldY, int worldZ, int surfaceY)
    {
        if (worldY <= Blocks.FlatMinY + 1 || worldY >= surfaceY - OverworldCaveCarver.SurfaceGuardDepth)
            return false;

        var bucket = BucketIndex(worldX, worldZ);
        if (bucket < 0)
            return false;

        var start = _bucketOffsets[bucket];
        var end = _bucketOffsets[bucket + 1];
        for (var i = start; i < end; i++)
        {
            if (_segments[_bucketIndices[i]].Contains(worldX, worldY, worldZ))
                return true;
        }

        return false;
    }

    /// <summary>Linear scan — tests only (ADR §69 spatial must match).</summary>
    internal bool IsCarvedBruteForce(int worldX, int worldY, int worldZ, int surfaceY)
    {
        if (worldY <= Blocks.FlatMinY + 1 || worldY >= surfaceY - OverworldCaveCarver.SurfaceGuardDepth)
            return false;

        for (var i = 0; i < _segments.Length; i++)
        {
            if (_segments[i].Contains(worldX, worldY, worldZ))
                return true;
        }

        return false;
    }

    private int BucketIndex(int worldX, int worldZ)
    {
        var lx = worldX - _originX;
        var lz = worldZ - _originZ;
        if ((uint)lx >= NeighborhoodBlocks || (uint)lz >= NeighborhoodBlocks)
            return -1;
        return (lz / CellSize) * Grid + (lx / CellSize);
    }

    private static void BuildSpatialIndex(
        CaveSegment[] segments,
        int originX,
        int originZ,
        out int[] offsets,
        out int[] indices)
    {
        Span<int> counts = stackalloc int[BucketCount];
        counts.Clear();
        for (var i = 0; i < segments.Length; i++)
            AccumulateBuckets(segments[i], originX, originZ, counts);

        offsets = new int[BucketCount + 1];
        var total = 0;
        for (var b = 0; b < BucketCount; b++)
        {
            offsets[b] = total;
            total += counts[b];
        }

        offsets[BucketCount] = total;
        indices = total == 0 ? Array.Empty<int>() : new int[total];
        counts.Clear();

        for (var i = 0; i < segments.Length; i++)
            ScatterBuckets(segments[i], originX, originZ, i, offsets, counts, indices);
    }

    private static void AccumulateBuckets(in CaveSegment seg, int originX, int originZ, Span<int> counts)
    {
        if (!TryBucketRange(seg, originX, originZ, out var minCx, out var maxCx, out var minCz, out var maxCz))
            return;

        for (var cz = minCz; cz <= maxCz; cz++)
        {
            for (var cx = minCx; cx <= maxCx; cx++)
                counts[cz * Grid + cx]++;
        }
    }

    private static void ScatterBuckets(
        in CaveSegment seg,
        int originX,
        int originZ,
        int segIndex,
        int[] offsets,
        Span<int> counts,
        int[] indices)
    {
        if (!TryBucketRange(seg, originX, originZ, out var minCx, out var maxCx, out var minCz, out var maxCz))
            return;

        for (var cz = minCz; cz <= maxCz; cz++)
        {
            for (var cx = minCx; cx <= maxCx; cx++)
            {
                var b = cz * Grid + cx;
                var slot = offsets[b] + counts[b]++;
                indices[slot] = segIndex;
            }
        }
    }

    private static bool TryBucketRange(
        in CaveSegment seg,
        int originX,
        int originZ,
        out int minCx,
        out int maxCx,
        out int minCz,
        out int maxCz)
    {
        var minX = Math.Min(seg.X0, seg.X1) - seg.Radius;
        var maxX = Math.Max(seg.X0, seg.X1) + seg.Radius;
        var minZ = Math.Min(seg.Z0, seg.Z1) - seg.Radius;
        var maxZ = Math.Max(seg.Z0, seg.Z1) + seg.Radius;

        if (maxX < originX || minX >= originX + NeighborhoodBlocks
            || maxZ < originZ || minZ >= originZ + NeighborhoodBlocks)
        {
            minCx = maxCx = minCz = maxCz = 0;
            return false;
        }

        minCx = Math.Clamp((minX - originX) / CellSize, 0, Grid - 1);
        maxCx = Math.Clamp((maxX - originX) / CellSize, 0, Grid - 1);
        minCz = Math.Clamp((minZ - originZ) / CellSize, 0, Grid - 1);
        maxCz = Math.Clamp((maxZ - originZ) / CellSize, 0, Grid - 1);
        return true;
    }
}

/// <summary>One worm step or cheese sphere bounds check.</summary>
readonly struct CaveSegment
{
    public readonly int X0, Y0, Z0, X1, Y1, Z1, Radius;

    public CaveSegment(int x0, int y0, int z0, int x1, int y1, int z1, int radius)
    {
        X0 = x0;
        Y0 = y0;
        Z0 = z0;
        X1 = x1;
        Y1 = y1;
        Z1 = z1;
        Radius = radius;
    }

    public bool Contains(int px, int py, int pz)
        => OverworldCaveCarver.IsWithinSegment(px, py, pz, X0, Y0, Z0, X1, Y1, Z1, Radius);
}
