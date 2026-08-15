using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class CaveCarverTests
{
    public CaveCarverTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Cave_carving_is_deterministic()
    {
        const int seed = 42;
        var a = OverworldCaveCarver.IsCarved(100, 24, -50, seed, surfaceY: 70);
        var b = OverworldCaveCarver.IsCarved(100, 24, -50, seed, surfaceY: 70);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Cave_respects_surface_guard()
    {
        const int seed = 7;
        var surface = OverworldTerrainSampler.SurfaceY(0, 0, seed);
        for (var y = surface - OverworldCaveCarver.SurfaceGuardDepth; y <= surface + 2; y++)
        {
            Assert.False(OverworldCaveCarver.IsCarved(0, y, 0, seed, surface));
        }
    }

    [Fact]
    public void Cave_context_matches_point_query()
    {
        const int seed = 99;
        const int chunkX = 3;
        const int chunkZ = -2;
        var ctx = OverworldCaveContext.ForColumn(chunkX, chunkZ, seed);
        try
        {
            for (var x = chunkX * 16; x < chunkX * 16 + 16; x++)
            {
                for (var z = chunkZ * 16; z < chunkZ * 16 + 16; z++)
                {
                    for (var y = 8; y < 48; y++)
                    {
                        var surface = OverworldTerrainSampler.SurfaceY(x, z, seed);
                        Assert.Equal(
                            ctx.IsCarvedBruteForce(x, y, z, surface),
                            ctx.IsCarved(x, y, z, surface));
                    }
                }
            }
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public void Cave_context_for_column_has_segments_and_disposes()
    {
        using var ctx = OverworldCaveContext.ForColumn(0, 0, seed: 42);
        Assert.True(ctx.SegmentCount > 0);
    }

    [Fact]
    public void Cave_column_mask_matches_segment_queries()
    {
        const int seed = 1776;
        const int chunkX = -3;
        const int chunkZ = 4;
        Span<int> surfaces = stackalloc int[256];
        Span<OverworldBiomeKind> biomes = stackalloc OverworldBiomeKind[256];
        OverworldTerrainSampler.FillColumnSurfaces(chunkX, chunkZ, seed, surfaces, biomes, out _);

        using var caves = OverworldCaveContext.ForColumn(chunkX, chunkZ, seed);
        caves.PrepareColumnMask(surfaces);
        for (var lx = 0; lx < 16; lx++)
        for (var lz = 0; lz < 16; lz++)
        {
            var x = (chunkX << 4) + lx;
            var z = (chunkZ << 4) + lz;
            var surface = surfaces[(lx << 4) | lz];
            for (var y = OverworldCaveContext.MaskMinY; y <= Math.Min(120, surface + 4); y++)
                Assert.Equal(caves.IsCarvedBruteForce(x, y, z, surface), caves.IsCarved(x, y, z, surface));
        }
    }

    [Fact]
    public void Segment_bounds_reject_points_outside_expanded_box()
    {
        Assert.False(OverworldCaveCarver.IsWithinSegment(
            px: 0, py: 0, pz: 4,
            x0: 0, y0: 0, z0: 0,
            x1: 3, y1: 0, z1: 0,
            radius: 1));
        Assert.True(OverworldCaveCarver.IsWithinSegment(
            px: 3, py: 1, pz: 0,
            x0: 0, y0: 0, z0: 0,
            x1: 3, y1: 0, z1: 0,
            radius: 1));
    }

    [Fact]
    public void Feature_plan_preserves_column_sampling()
    {
        const int seed = 31415;
        const int chunkX = -2;
        const int chunkZ = 3;
        Span<int> surfaces = stackalloc int[256];
        Span<OverworldBiomeKind> biomes = stackalloc OverworldBiomeKind[256];
        OverworldTerrainSampler.FillColumnSurfaces(chunkX, chunkZ, seed, surfaces, biomes, out _);

        using var caves = OverworldCaveContext.ForColumn(chunkX, chunkZ, seed);
        var features = OverworldTerrainSampler.FeaturePlacementPlan.Build(chunkX, chunkZ, seed);
        Assert.Equal(
            OverworldTerrainSampler.MaxTreeCanopyYAffectingChunk(chunkX, chunkZ, seed),
            OverworldTerrainSampler.MaxTreeCanopyYAffectingChunk(chunkX, chunkZ, seed, features));
        for (var lx = 0; lx < 16; lx++)
        for (var lz = 0; lz < 16; lz++)
        {
            var x = (chunkX << 4) + lx;
            var z = (chunkZ << 4) + lz;
            var index = (lx << 4) | lz;
            for (var y = -16; y <= 96; y += 4)
            {
                var uncached = OverworldTerrainSampler.SampleNoiseBlockAtSurface(
                    x, y, z, seed, surfaces[index], caves, biomes[index]);
                var cached = OverworldTerrainSampler.SampleNoiseBlockAtSurface(
                    x, y, z, seed, surfaces[index], caves, biomes[index], features);
                Assert.Equal(uncached, cached);
            }
        }
    }

    [Fact]
    public void Cave_region_has_connected_air_volume()
    {
        const int seed = 21;
        var start = FindCarvedCell(seed);
        Assert.True(start.HasValue);

        var (sx, sy, sz) = start.Value;
        var visited = new HashSet<(int X, int Y, int Z)>();
        var queue = new Queue<(int X, int Y, int Z)>();
        queue.Enqueue((sx, sy, sz));
        visited.Add((sx, sy, sz));

        while (queue.Count > 0 && visited.Count < 64)
        {
            var (x, y, z) = queue.Dequeue();
            foreach (var (nx, ny, nz) in Neighbors(x, y, z))
            {
                if (visited.Contains((nx, ny, nz))) continue;
                var surface = OverworldTerrainSampler.SurfaceY(nx, nz, seed);
                if (!OverworldCaveCarver.IsCarved(nx, ny, nz, seed, surface)) continue;
                visited.Add((nx, ny, nz));
                queue.Enqueue((nx, ny, nz));
            }
        }

        Assert.True(visited.Count >= 12, $"expected connected cave volume, got {visited.Count} cells");
    }

    [Fact]
    public void Column_build_matches_SampleBaseBlock_with_caves()
    {
        const int seed = 12;
        var noise = new NoiseTerrainProvider(seed);
        var world = new World.World(new InMemoryChunkStorage(), terrain: noise);
        for (var x = -4; x < 20; x++)
        {
            for (var z = -4; z < 20; z++)
            {
                for (var y = Blocks.FlatMinY; y <= 80; y += 2)
                    Assert.Equal(noise.SampleBaseBlock(x, y, z), world.GetBlock(x, y, z));
            }
        }
    }

    private static (int X, int Y, int Z)? FindCarvedCell(int seed)
    {
        for (var x = -64; x < 64; x++)
        {
            for (var z = -64; z < 64; z++)
            {
                var surface = OverworldTerrainSampler.SurfaceY(x, z, seed);
                for (var y = Blocks.FlatMinY + 2; y < surface - OverworldCaveCarver.SurfaceGuardDepth; y++)
                {
                    if (OverworldCaveCarver.IsCarved(x, y, z, seed, surface))
                        return (x, y, z);
                }
            }
        }

        return null;
    }

    private static IEnumerable<(int X, int Y, int Z)> Neighbors(int x, int y, int z)
    {
        yield return (x + 1, y, z);
        yield return (x - 1, y, z);
        yield return (x, y + 1, z);
        yield return (x, y - 1, z);
        yield return (x, y, z + 1);
        yield return (x, y, z - 1);
    }
}
