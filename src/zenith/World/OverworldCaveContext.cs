using System.Runtime.CompilerServices;
using System.Buffers;

namespace Zenith.World;

/// <summary>
/// Precomputed worm segments for a column neighborhood (ADR §65 / §69 / §75).
/// Built once per noise column; XZ spatial grid for O(nearby) carve tests.
/// Dispose returns pooled segment/index buffers (column path must <c>using</c>).
/// </summary>
sealed class OverworldCaveContext : IDisposable
{
    internal const int CellSize = 8;
    internal const int NeighborhoodChunks = 3;
    internal const int NeighborhoodBlocks = NeighborhoodChunks * 16;
    internal const int Grid = NeighborhoodBlocks / CellSize; // 6
    internal const int BucketCount = Grid * Grid;
    internal const int MaskMinY = Blocks.FlatMinY + 2;
    internal const int MaskMaxY = 320;
    private const int MaskHeight = MaskMaxY - MaskMinY + 1;
    private const int MaskWordsPerColumn = (MaskHeight + 63) / 64;

    [ThreadStatic] private static List<CaveSegment>? t_scratch;

    private CaveSegment[] _segments = Array.Empty<CaveSegment>();
    private int _segmentCount;
    private int[] _bucketOffsets = EmptyOffsets;
    private int[] _bucketIndices = Array.Empty<int>();
    private int _indexCount;
    private int _originX;
    private int _originZ;
    private ulong[] _columnMask = Array.Empty<ulong>();
    private int _columnMaskLength;
    private bool _disposed;

    private static readonly int[] EmptyOffsets = CreateEmptyOffsets();

    private static int[] CreateEmptyOffsets()
    {
        var o = new int[BucketCount + 1];
        return o;
    }

    private OverworldCaveContext() { }

    public static OverworldCaveContext ForColumn(int chunkX, int chunkZ, int seed)
    {
        var ctx = new OverworldCaveContext();
        ctx.Build(chunkX, chunkZ, seed);
        return ctx;
    }

    private void Build(int chunkX, int chunkZ, int seed)
    {
        var list = t_scratch ??= new List<CaveSegment>(512);
        list.Clear();
        for (var dcx = -1; dcx <= 1; dcx++)
        {
            for (var dcz = -1; dcz <= 1; dcz++)
                OverworldCaveCarver.CollectSegments(chunkX + dcx, chunkZ + dcz, seed, list);
        }

        _segmentCount = list.Count;
        if (_segmentCount == 0)
        {
            _segments = Array.Empty<CaveSegment>();
        }
        else
        {
            _segments = ArrayPool<CaveSegment>.Shared.Rent(_segmentCount);
            list.CopyTo(0, _segments, 0, _segmentCount);
        }

        _originX = (chunkX - 1) * 16;
        _originZ = (chunkZ - 1) * 16;
        _bucketOffsets = new int[BucketCount + 1];
        BuildSpatialIndex();
    }

    /// <summary>Worm segment count in the 3×3 neighborhood (bench / diagnostics).</summary>
    public int SegmentCount => _segmentCount;

    public bool IsCarved(int worldX, int worldY, int worldZ, int surfaceY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (worldY <= Blocks.FlatMinY + 1 || worldY >= surfaceY - OverworldCaveCarver.SurfaceGuardDepth)
            return false;

        if (_columnMaskLength != 0
            && (uint)(worldX - (_originX + 16)) < 16u
            && (uint)(worldZ - (_originZ + 16)) < 16u)
            return IsMasked(worldX, worldY, worldZ);

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

    /// <summary>
    /// Materializes the cave result for this column once the surface field is available.
    /// The mask is intentionally owned by this short-lived context: it removes repeated
    /// segment intersection work from the 16×16×height voxel loop without changing the
    /// point-sampling API used by callers outside full-column generation.
    /// </summary>
    internal void PrepareColumnMask(ReadOnlySpan<int> surfaces)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (surfaces.Length < 256)
            throw new ArgumentException("A cave column mask requires 256 surface samples.", nameof(surfaces));

        var required = 256 * MaskWordsPerColumn;
        if (_columnMask.Length < required)
            _columnMask = ArrayPool<ulong>.Shared.Rent(required);
        Array.Clear(_columnMask, 0, required);
        _columnMaskLength = required;

        var columnMinX = _originX + 16;
        var columnMinZ = _originZ + 16;
        var columnMaxX = columnMinX + 15;
        var columnMaxZ = columnMinZ + 15;

        for (var segmentIndex = 0; segmentIndex < _segmentCount; segmentIndex++)
        {
            ref readonly var segment = ref _segments[segmentIndex];
            var minX = Math.Max(segment.MinX, columnMinX);
            var maxX = Math.Min(segment.MaxX, columnMaxX);
            var minZ = Math.Max(segment.MinZ, columnMinZ);
            var maxZ = Math.Min(segment.MaxZ, columnMaxZ);
            if (minX > maxX || minZ > maxZ)
                continue;

            for (var worldX = minX; worldX <= maxX; worldX++)
            {
                var localX = worldX - columnMinX;
                for (var worldZ = minZ; worldZ <= maxZ; worldZ++)
                {
                    var localZ = worldZ - columnMinZ;
                    var surface = surfaces[(localX << 4) | localZ];
                    var minY = Math.Max(segment.MinY, MaskMinY);
                    var maxY = Math.Min(segment.MaxY, Math.Min(MaskMaxY,
                        surface - OverworldCaveCarver.SurfaceGuardDepth - 1));
                    if (minY > maxY)
                        continue;

                    var columnOffset = ((localX << 4) | localZ) * MaskWordsPerColumn;
                    for (var worldY = minY; worldY <= maxY; worldY++)
                    {
                        if (!segment.Contains(worldX, worldY, worldZ))
                            continue;

                        var maskY = worldY - MaskMinY;
                        _columnMask[columnOffset + (maskY >> 6)] |= 1UL << (maskY & 63);
                    }
                }
            }
        }
    }

    /// <summary>Linear scan — tests only (ADR §69 spatial must match).</summary>
    internal bool IsCarvedBruteForce(int worldX, int worldY, int worldZ, int surfaceY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (worldY <= Blocks.FlatMinY + 1 || worldY >= surfaceY - OverworldCaveCarver.SurfaceGuardDepth)
            return false;

        for (var i = 0; i < _segmentCount; i++)
        {
            if (_segments[i].Contains(worldX, worldY, worldZ))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_segmentCount > 0 && _segments.Length > 0)
        {
            ArrayPool<CaveSegment>.Shared.Return(_segments, clearArray: false);
            _segments = Array.Empty<CaveSegment>();
            _segmentCount = 0;
        }

        if (_indexCount > 0 && _bucketIndices.Length > 0)
        {
            ArrayPool<int>.Shared.Return(_bucketIndices, clearArray: false);
            _bucketIndices = Array.Empty<int>();
            _indexCount = 0;
        }

        if (_columnMaskLength > 0 && _columnMask.Length > 0)
        {
            ArrayPool<ulong>.Shared.Return(_columnMask, clearArray: false);
            _columnMask = Array.Empty<ulong>();
            _columnMaskLength = 0;
        }

        _bucketOffsets = EmptyOffsets;
    }

    private int BucketIndex(int worldX, int worldZ)
    {
        var lx = worldX - _originX;
        var lz = worldZ - _originZ;
        if ((uint)lx >= NeighborhoodBlocks || (uint)lz >= NeighborhoodBlocks)
            return -1;
        return (lz / CellSize) * Grid + (lx / CellSize);
    }

    private void BuildSpatialIndex()
    {
        Span<int> counts = stackalloc int[BucketCount];
        counts.Clear();
        for (var i = 0; i < _segmentCount; i++)
            AccumulateBuckets(in _segments[i], _originX, _originZ, counts);

        var total = 0;
        for (var b = 0; b < BucketCount; b++)
        {
            _bucketOffsets[b] = total;
            total += counts[b];
        }

        _bucketOffsets[BucketCount] = total;
        _indexCount = total;
        if (total == 0)
        {
            _bucketIndices = Array.Empty<int>();
            return;
        }

        _bucketIndices = ArrayPool<int>.Shared.Rent(total);
        counts.Clear();

        for (var i = 0; i < _segmentCount; i++)
            ScatterBuckets(in _segments[i], _originX, _originZ, i, _bucketOffsets, counts, _bucketIndices);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryBucketRange(
        in CaveSegment seg,
        int originX,
        int originZ,
        out int minCx,
        out int maxCx,
        out int minCz,
        out int maxCz)
    {
        var minX = seg.MinX;
        var maxX = seg.MaxX;
        var minZ = seg.MinZ;
        var maxZ = seg.MaxZ;

        if (maxX < originX || minX >= originX + NeighborhoodBlocks
            || maxZ < originZ || minZ >= originZ + NeighborhoodBlocks)
        {
            minCx = maxCx = minCz = maxCz = 0;
            return false;
        }

        minCx = ClampBucket((minX - originX) / CellSize);
        maxCx = ClampBucket((maxX - originX) / CellSize);
        minCz = ClampBucket((minZ - originZ) / CellSize);
        maxCz = ClampBucket((maxZ - originZ) / CellSize);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsMasked(int worldX, int worldY, int worldZ)
    {
        if ((uint)(worldY - MaskMinY) >= MaskHeight)
            return false;

        var localX = worldX - (_originX + 16);
        var localZ = worldZ - (_originZ + 16);
        var columnOffset = ((localX << 4) | localZ) * MaskWordsPerColumn;
        var maskY = worldY - MaskMinY;
        return (_columnMask[columnOffset + (maskY >> 6)] & (1UL << (maskY & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampBucket(int value)
    {
        if (value < 0) return 0;
        return value >= Grid ? Grid - 1 : value;
    }
}

/// <summary>One worm step or cheese sphere bounds check.</summary>
readonly struct CaveSegment
{
    public readonly int X0, Y0, Z0, X1, Y1, Z1, Radius;
    public readonly int Dx, Dy, Dz, LengthSquared;
    public readonly int MinX, MaxX, MinY, MaxY, MinZ, MaxZ;

    public CaveSegment(int x0, int y0, int z0, int x1, int y1, int z1, int radius)
    {
        X0 = x0;
        Y0 = y0;
        Z0 = z0;
        X1 = x1;
        Y1 = y1;
        Z1 = z1;
        Radius = radius;
        Dx = x1 - x0;
        Dy = y1 - y0;
        Dz = z1 - z0;
        LengthSquared = Dx * Dx + Dy * Dy + Dz * Dz;
        var minX = x0 < x1 ? x0 : x1;
        var maxX = x0 > x1 ? x0 : x1;
        var minZ = z0 < z1 ? z0 : z1;
        var maxZ = z0 > z1 ? z0 : z1;
        MinX = minX - radius;
        MaxX = maxX + radius;
        var minY = y0 < y1 ? y0 : y1;
        var maxY = y0 > y1 ? y0 : y1;
        MinY = minY - radius;
        MaxY = maxY + radius;
        MinZ = minZ - radius;
        MaxZ = maxZ + radius;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int px, int py, int pz)
        => OverworldCaveCarver.IsWithinSegment(
            px, py, pz, X0, Y0, Z0, X1, Y1, Z1, Radius,
            MinX, MaxX, MinY, MaxY, MinZ, MaxZ, Dx, Dy, Dz, LengthSquared);
}
