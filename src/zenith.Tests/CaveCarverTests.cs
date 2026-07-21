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
